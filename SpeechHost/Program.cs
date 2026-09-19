using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace CultistAccessibility.SpeechHost
{
    /// <summary>
    /// Line protocol on stdin (UTF-8, one command per line):
    ///   O&lt;0|1&gt;text   speak + braille (prism_backend_output), 1 = interrupt
    ///   S&lt;0|1&gt;text   speech only
    ///   Btext        braille only
    ///   X            stop speech
    ///   R            re-evaluate the best backend now
    ///   Q            quit
    /// Status lines on stdout: "I ..." info, "W ..." warning, "E ..." error.
    /// The host exits when stdin closes, on Q, or when the parent process (--parent pid) exits.
    /// All Prism backend calls happen on the main thread of this process.
    /// </summary>
    internal static class Program
    {
        private static readonly BlockingCollection<string> Commands = new BlockingCollection<string>();
        private static StreamWriter _out;
        private static IntPtr _ctx;
        private static IntPtr _backend;
        private static ulong _features;
        private static string _backendName = "";
        private static volatile int _backendPriority = -1;
        private static volatile bool _reevaluate;
        private static DateTime _lastRecreate = DateTime.MinValue;
        private static PrismNative.AvailabilityCallback _callbackKeepAlive;

        private static int Main(string[] args)
        {
            _out = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
            int parentPid = -1;
            string prismPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "prism.dll");
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--parent") int.TryParse(args[i + 1], out parentPid);
                if (args[i] == "--prism") prismPath = args[i + 1];
                if (args[i] == "--backend" && !string.Equals(args[i + 1], "Auto", StringComparison.OrdinalIgnoreCase)) _forcedBackend = args[i + 1];
            }

            if (PrismNative.LoadLibrary(prismPath) == IntPtr.Zero)
            {
                Report("E", "could not load " + prismPath + " (win32 error " + System.Runtime.InteropServices.Marshal.GetLastWin32Error() + ")");
                return 2;
            }

            try
            {
                Report("I", "prism " + PrismNative.FromUtf8(PrismNative.prism_version_string()));
                InitContext();
                if (_ctx == IntPtr.Zero)
                {
                    Report("E", "prism_init returned null");
                    return 3;
                }
                CreateBackend("startup");
            }
            catch (Exception ex)
            {
                Report("E", "init failed: " + ex.GetType().Name + ": " + ex.Message);
                return 4;
            }

            var reader = new Thread(ReadStdin) { IsBackground = true, Name = "stdin" };
            reader.Start();

            Process parent = null;
            if (parentPid > 0)
            {
                try { parent = Process.GetProcessById(parentPid); } catch { parent = null; }
            }

            var lastParentCheck = DateTime.UtcNow;
            bool quit = false;
            while (!quit)
            {
                if (Commands.TryTake(out var cmd, 200))
                {
                    quit = Handle(cmd);
                }
                else if (Commands.IsAddingCompleted)
                {
                    break;
                }

                if (_reevaluate)
                {
                    _reevaluate = false;
                    Reevaluate();
                }

                if (parent != null && (DateTime.UtcNow - lastParentCheck).TotalSeconds > 2)
                {
                    lastParentCheck = DateTime.UtcNow;
                    try { if (parent.HasExited) break; } catch { break; }
                }
            }

            Shutdown();
            return 0;
        }

        private static void InitContext()
        {
            var cfg = PrismNative.prism_config_init();
            _callbackKeepAlive = OnAvailabilityChanged;
            cfg.availability_callback = System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(_callbackKeepAlive);
            cfg.availability_userdata = IntPtr.Zero;
            cfg.availability_poll_interval_ms = 1000;
            cfg.availability_debounce_samples = 2;
            cfg.availability_backoff_max_ms = 4000;
            _ctx = PrismNative.prism_init(ref cfg);
            if (_ctx == IntPtr.Zero)
            {
                // Fall back to a context without availability polling.
                Report("W", "prism_init with availability polling failed; retrying without it");
                _ctx = PrismNative.prism_init(IntPtr.Zero);
            }
        }

        /// <summary>Runs on Prism's poll thread: only flags work for the main loop.</summary>
        private static void OnAvailabilityChanged(IntPtr userdata, ulong backendId, IntPtr name, bool available)
        {
            try
            {
                int priority = PrismNative.prism_registry_priority(_ctx, backendId);
                string backendName = PrismNative.FromUtf8(name) ?? "";
                if (available && priority > _backendPriority)
                {
                    _reevaluate = true;
                }
                else if (!available && string.Equals(backendName, _backendName, StringComparison.Ordinal))
                {
                    _reevaluate = true;
                }
            }
            catch
            {
                _reevaluate = true;
            }
        }

        private static void ReadStdin()
        {
            try
            {
                using (var reader = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false)))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        Commands.Add(line);
                    }
                }
            }
            catch
            {
                // Broken pipe: the game is gone.
            }
            Commands.CompleteAdding();
        }

        private static bool Handle(string cmd)
        {
            if (string.IsNullOrEmpty(cmd)) return false;
            char op = cmd[0];
            switch (op)
            {
                case 'O':
                case 'S':
                {
                    if (cmd.Length < 2) return false;
                    bool interrupt = cmd[1] == '1';
                    string text = cmd.Length > 2 ? cmd.Substring(2) : "";
                    if (text.Trim().Length == 0) return false;
                    Speak(text, interrupt, op == 'O', allowRetry: true);
                    return false;
                }
                case 'B':
                    Braille(cmd.Substring(1));
                    return false;
                case 'X':
                    if (_backend != IntPtr.Zero && (_features & PrismNative.FEATURE_SUPPORTS_STOP) != 0)
                        PrismNative.prism_backend_stop(_backend);
                    return false;
                case 'R':
                    Reevaluate();
                    return false;
                case 'Q':
                    return true;
                default:
                    Report("W", "unknown command '" + op + "'");
                    return false;
            }
        }

        private static void Speak(string text, bool interrupt, bool withBraille, bool allowRetry)
        {
            if (_backend == IntPtr.Zero)
            {
                if (!TryRecreate("no backend")) return;
            }
            byte[] utf8 = PrismNative.Utf8Z(text);
            int result;
            if (withBraille && (_features & PrismNative.FEATURE_SUPPORTS_OUTPUT) != 0)
                result = PrismNative.prism_backend_output(_backend, utf8, interrupt);
            else
                result = PrismNative.prism_backend_speak(_backend, utf8, interrupt);

            if (result != PrismNative.PRISM_OK)
            {
                Report("W", "speak failed on " + _backendName + ": " + PrismNative.ErrorString(result));
                if (allowRetry && TryRecreate("speak error"))
                {
                    Speak(text, interrupt, withBraille, allowRetry: false);
                }
            }
        }

        private static void Braille(string text)
        {
            if (_backend == IntPtr.Zero || (_features & PrismNative.FEATURE_SUPPORTS_BRAILLE) == 0) return;
            int result = PrismNative.prism_backend_braille(_backend, PrismNative.Utf8Z(text));
            if (result != PrismNative.PRISM_OK)
                Report("W", "braille failed: " + PrismNative.ErrorString(result));
        }

        /// <summary>Rate-limited: free the backend and create the best one again.</summary>
        private static bool TryRecreate(string reason)
        {
            if ((DateTime.UtcNow - _lastRecreate).TotalSeconds < 3) return _backend != IntPtr.Zero;
            _lastRecreate = DateTime.UtcNow;
            FreeBackend();
            CreateBackend(reason);
            return _backend != IntPtr.Zero;
        }

        private static string _forcedBackend;

        /// <summary>The backend named by --backend (for example SAPI), initialised; null when unavailable.</summary>
        private static IntPtr CreateForced()
        {
            if (string.IsNullOrEmpty(_forcedBackend)) return IntPtr.Zero;
            ulong id = PrismNative.prism_registry_id(_ctx, PrismNative.Utf8Z(_forcedBackend));
            if (id == 0)
            {
                Report("W", "unknown backend '" + _forcedBackend + "', using the best available one");
                return IntPtr.Zero;
            }
            IntPtr b = PrismNative.prism_registry_create(_ctx, id);
            if (b == IntPtr.Zero) return IntPtr.Zero;
            int err = PrismNative.prism_backend_initialize(b);
            if (err != PrismNative.PRISM_OK)
            {
                Report("W", "backend " + _forcedBackend + " failed to initialise: " + PrismNative.ErrorString(err) + "; using the best available one");
                PrismNative.prism_backend_free(b);
                return IntPtr.Zero;
            }
            return b;
        }

        private static void CreateBackend(string reason)
        {
            _backend = CreateForced();
            if (_backend == IntPtr.Zero) _backend = PrismNative.prism_registry_create_best(_ctx);
            if (_backend == IntPtr.Zero)
            {
                _backendName = "";
                _backendPriority = -1;
                Report("E", "no speech backend available (" + reason + ")");
                return;
            }
            AdoptBackendInfo();
            Report("I", "backend " + _backendName + " (" + reason + ") features=0x" + _features.ToString("X"));
        }

        private static void AdoptBackendInfo()
        {
            _features = PrismNative.prism_backend_get_features(_backend);
            _backendName = PrismNative.FromUtf8(PrismNative.prism_backend_name(_backend)) ?? "unknown";
            ulong id = PrismNative.prism_registry_id(_ctx, PrismNative.Utf8Z(_backendName));
            _backendPriority = id != 0 ? PrismNative.prism_registry_priority(_ctx, id) : -1;
        }

        /// <summary>Swap to a better backend if one became available (for example NVDA started after the game).</summary>
        private static void Reevaluate()
        {
            if (!string.IsNullOrEmpty(_forcedBackend) && _backend != IntPtr.Zero && string.Equals(_backendName, _forcedBackend, StringComparison.OrdinalIgnoreCase))
                return;
            IntPtr candidate = CreateForced();
            if (candidate == IntPtr.Zero) candidate = PrismNative.prism_registry_create_best(_ctx);
            if (candidate == IntPtr.Zero)
            {
                if (_backend != IntPtr.Zero)
                {
                    // Current backend may still work; keep it.
                    return;
                }
                Report("E", "no speech backend available (re-evaluate)");
                return;
            }
            string name = PrismNative.FromUtf8(PrismNative.prism_backend_name(candidate)) ?? "unknown";
            if (_backend != IntPtr.Zero && string.Equals(name, _backendName, StringComparison.Ordinal))
            {
                PrismNative.prism_backend_free(candidate);
                return;
            }
            FreeBackend();
            _backend = candidate;
            AdoptBackendInfo();
            Report("I", "backend " + _backendName + " (availability changed) features=0x" + _features.ToString("X"));
        }

        private static void FreeBackend()
        {
            if (_backend != IntPtr.Zero)
            {
                try { PrismNative.prism_backend_free(_backend); } catch { }
                _backend = IntPtr.Zero;
            }
        }

        private static void Shutdown()
        {
            try
            {
                if (_backend != IntPtr.Zero && (_features & PrismNative.FEATURE_SUPPORTS_STOP) != 0)
                    PrismNative.prism_backend_stop(_backend);
            }
            catch { }
            FreeBackend();
            if (_ctx != IntPtr.Zero)
            {
                try { PrismNative.prism_shutdown(_ctx); } catch { }
                _ctx = IntPtr.Zero;
            }
        }

        private static void Report(string level, string message)
        {
            try { _out.WriteLine(level + " " + message.Replace('\n', ' ').Replace('\r', ' ')); }
            catch { }
        }
    }
}
