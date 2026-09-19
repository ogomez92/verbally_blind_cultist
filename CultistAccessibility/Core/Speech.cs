using System;
using System.IO;
using System.Text;
using CultistAccessibility.Core.Buffers;
using CultistAccessibility.Core.Output;
using UnityEngine;

namespace CultistAccessibility.Core
{
    /// <summary>
    /// The only speech entry point (the template's ScreenReaderOutput).
    /// Say* = focus echoes and on-demand readouts. SayEvent = unsolicited announcements, which are also
    /// recorded in the Events buffer and the event log file next to the plugin.
    /// Speech is queued by default; nothing interrupts unless the player enabled InterruptOnNavigation.
    /// </summary>
    internal static class Speech
    {
        private static SpeechHostClient _client;
        private static StreamWriter _eventLog;
        private static string _lastEventText;
        private static float _lastEventTime;

        public static string LastSpoken { get; private set; } = "";

        public static void Initialize(string pluginDir)
        {
            string exe = Path.Combine(Path.Combine(pluginDir, "SpeechHost"), "CultistAccessibility.SpeechHost.exe");
            _client = new SpeechHostClient(exe, ModConfig.SpeechBackend?.Value);
            _client.EnsureStarted();
            try
            {
                string logPath = Path.Combine(pluginDir, "CultistAccessibility_events.log");
                _eventLog = new StreamWriter(logPath, false, new UTF8Encoding(false)) { AutoFlush = true };
                _eventLog.WriteLine("Cultist Simulator Accessibility event log, " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            }
            catch (Exception ex)
            {
                Plugin.LogWarning("Could not open event log: " + ex.Message);
            }
        }

        /// <summary>Speak on demand (hotkeys, readouts). Queued.</summary>
        public static void Say(string text, bool interrupt = false)
        {
            string clean = TextCleaner.Clean(text);
            if (string.IsNullOrEmpty(clean)) return;
            LastSpoken = clean;
            Plugin.LogDebug("Spoke: " + clean);
            if (_client == null) return;
            bool braille = ModConfig.Braille == null || ModConfig.Braille.Value;
            _client.Send(braille ? 'O' : 'S', interrupt, clean);
        }

        /// <summary>Focus echo from arrow-key navigation.</summary>
        public static void SayFocus(string text)
        {
            Say(text, ModConfig.InterruptOnNavigation != null && ModConfig.InterruptOnNavigation.Value);
        }

        /// <summary>Unsolicited announcement: spoken, recorded in the Events buffer and the event log.</summary>
        public static void SayEvent(string text)
        {
            string clean = TextCleaner.Clean(text);
            if (string.IsNullOrEmpty(clean)) return;
            float now = Time.unscaledTime;
            if (clean == _lastEventText && now - _lastEventTime < 0.75f) return;
            _lastEventText = clean;
            _lastEventTime = now;
            LogEvent(clean);
            Say(clean, false);
        }

        /// <summary>Record an event without speaking it.</summary>
        public static void LogEvent(string text)
        {
            string clean = TextCleaner.Clean(text);
            if (string.IsNullOrEmpty(clean)) return;
            BufferManager.AddEvent(clean);
            try { _eventLog?.WriteLine(DateTime.Now.ToString("HH:mm:ss") + " " + clean); } catch { }
        }

        public static void Stop()
        {
            _client?.SendRaw("X");
        }

        /// <summary>Main thread: forward host status lines to the BepInEx log.</summary>
        public static void Update()
        {
            if (_client == null) return;
            while (_client.TryDequeueMessage(out var msg))
            {
                if (msg.StartsWith("E ")) Plugin.LogError("SpeechHost: " + msg.Substring(2));
                else if (msg.StartsWith("W ")) Plugin.LogWarning("SpeechHost: " + msg.Substring(2));
                else Plugin.LogInfo("SpeechHost: " + (msg.Length > 2 ? msg.Substring(2) : msg));
            }
        }

        public static string BackendName => _client?.BackendName ?? "";

        public static void Shutdown()
        {
            try { _client?.Shutdown(); } catch { }
            try { _eventLog?.Dispose(); } catch { }
            _eventLog = null;
        }
    }
}
