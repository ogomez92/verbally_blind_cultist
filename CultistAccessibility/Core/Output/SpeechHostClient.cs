using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace CultistAccessibility.Core.Output
{
    /// <summary>
    /// Owns the x64 speech host process (SpeechHost\CultistAccessibility.SpeechHost.exe) and the pipe to it.
    /// The game is 32-bit and cannot load the x64-only prism.dll, so Prism lives in that process.
    /// Only <see cref="Core.Speech"/> uses this class.
    /// </summary>
    internal sealed class SpeechHostClient
    {
        private readonly string _exePath;
        private readonly object _lock = new object();
        private readonly ConcurrentQueue<string> _hostMessages = new ConcurrentQueue<string>();
        private Process _process;
        private Stream _stdin;
        private DateTime _lastStartAttempt = DateTime.MinValue;
        // Counts starts that have not yet reported a backend: a host that exits at once (bad prism.dll,
        // init failure) must back off instead of being respawned for every spoken line.
        private int _startFailures;

        public string BackendName { get; private set; } = "";

        private readonly string _backend;

        public SpeechHostClient(string exePath, string backend)
        {
            _exePath = exePath;
            _backend = string.IsNullOrWhiteSpace(backend) ? "Auto" : backend.Trim();
        }

        public bool IsRunning
        {
            get
            {
                try { return _process != null && !_process.HasExited && _stdin != null; }
                catch { return false; }
            }
        }

        /// <summary>Starts the host if it is not running. Rate limited so a missing exe or a crashing host does not spin.</summary>
        public bool EnsureStarted()
        {
            if (IsRunning) return true;
            int failures = Volatile.Read(ref _startFailures);
            double wait = failures == 0 ? 0 : Math.Min(30, 2 * failures);
            if ((DateTime.UtcNow - _lastStartAttempt).TotalSeconds < wait) return false;
            _lastStartAttempt = DateTime.UtcNow;
            return Start();
        }

        private bool Start()
        {
            lock (_lock)
            {
                CleanupProcess();
                // Counted as failed until the host prints its "I backend" line (see ReadOutput).
                Interlocked.Increment(ref _startFailures);
                if (!File.Exists(_exePath))
                {
                    _hostMessages.Enqueue("E speech host not found at " + _exePath);
                    return false;
                }
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = _exePath,
                        Arguments = "--parent " + Process.GetCurrentProcess().Id + " --backend \"" + _backend.Replace("\"", "") + "\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = false,
                        WorkingDirectory = Path.GetDirectoryName(_exePath) ?? ""
                    };
                    _process = Process.Start(psi);
                    if (_process == null) throw new InvalidOperationException("Process.Start returned null");
                    _stdin = _process.StandardInput.BaseStream;
                    var stdout = _process.StandardOutput.BaseStream;
                    var reader = new Thread(() => ReadOutput(stdout)) { IsBackground = true, Name = "SpeechHostReader" };
                    reader.Start();
                    return true;
                }
                catch (Exception ex)
                {
                    _hostMessages.Enqueue("E failed to start speech host: " + ex.Message);
                    CleanupProcess();
                    return false;
                }
            }
        }

        private void ReadOutput(Stream stdout)
        {
            try
            {
                using (var reader = new StreamReader(stdout, new UTF8Encoding(false)))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.StartsWith("I backend ", StringComparison.Ordinal))
                        {
                            string rest = line.Substring("I backend ".Length);
                            int paren = rest.IndexOf(" (", StringComparison.Ordinal);
                            BackendName = paren > 0 ? rest.Substring(0, paren) : rest;
                            Interlocked.Exchange(ref _startFailures, 0);
                        }
                        _hostMessages.Enqueue(line);
                    }
                }
            }
            catch
            {
                // Process ended.
            }
        }

        /// <summary>Messages from the host, drained on the main thread for logging.</summary>
        public bool TryDequeueMessage(out string message) => _hostMessages.TryDequeue(out message);

        public void Send(char op, bool interrupt, string text)
        {
            string line = op + (interrupt ? "1" : "0") + Flatten(text);
            WriteLine(line);
        }

        public void SendRaw(string line) => WriteLine(line);

        private void WriteLine(string line)
        {
            if (!EnsureStarted()) return;
            byte[] bytes = Encoding.UTF8.GetBytes(line + "\n");
            lock (_lock)
            {
                try
                {
                    _stdin.Write(bytes, 0, bytes.Length);
                    _stdin.Flush();
                }
                catch (Exception ex)
                {
                    _hostMessages.Enqueue("W speech host pipe failed: " + ex.Message);
                    CleanupProcess();
                }
            }
        }

        private static string Flatten(string text)
        {
            if (text.IndexOf('\n') < 0 && text.IndexOf('\r') < 0) return text;
            return text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
        }

        public void Shutdown()
        {
            lock (_lock)
            {
                try
                {
                    if (_stdin != null)
                    {
                        byte[] q = Encoding.UTF8.GetBytes("Q\n");
                        _stdin.Write(q, 0, q.Length);
                        _stdin.Flush();
                    }
                }
                catch { }
                CleanupProcess();
            }
        }

        private void CleanupProcess()
        {
            try { _stdin?.Dispose(); } catch { }
            _stdin = null;
            if (_process != null)
            {
                try
                {
                    if (!_process.HasExited && !_process.WaitForExit(300))
                        _process.Kill();
                }
                catch { }
                try { _process.Dispose(); } catch { }
                _process = null;
            }
        }
    }
}
