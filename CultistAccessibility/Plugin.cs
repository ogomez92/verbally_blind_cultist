using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using CultistAccessibility.Core;
using CultistAccessibility.Core.Buffers;
using CultistAccessibility.Help;
using CultistAccessibility.Patches;
using HarmonyLib;
using UnityEngine;

namespace CultistAccessibility
{
    [BepInPlugin(Guid, ModName, ModVersion)]
    [BepInProcess("cultistsimulator.exe")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "accessibility.cultistsimulator.screenreader";
        public const string ModName = "Cultist Simulator Accessibility";
        public const string ModVersion = "1.1.0";

        internal static Plugin Instance { get; private set; }
        internal static ManualLogSource Log { get; private set; }
        internal static string PluginDir { get; private set; }
        internal static Harmony Harmony { get; private set; }

        private GameObject _host;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            PluginDir = Path.GetDirectoryName(Info.Location);

            try
            {
                ModConfig.Init(Config);
                Speech.Initialize(PluginDir);
                BufferManager.Initialize();
                BufferManager.Verbs.Refresher = Tabletop.StatusReader.VerbLines;
                BufferManager.Table.Refresher = Tabletop.StatusReader.TableLines;
                BufferManager.Status.Refresher = Tabletop.StatusReader.StatusLines;
                ApplyPatches();
                CreateHandlers();
                HelpSystem.RegisterDefaultContexts();
                Speech.Say(Strings.ModLoaded);
                LogInfo(ModName + " " + ModVersion + " loaded");
            }
            catch (Exception ex)
            {
                LogError("Startup failed: " + ex);
            }
        }

        private void ApplyPatches()
        {
            Harmony = new Harmony(Guid);
            PatchRegistry.ApplyAll(Harmony);
        }

        private void CreateHandlers()
        {
            _host = new GameObject("CultistAccessibility");
            _host.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(_host);
            _host.AddComponent<AccessibilityController>();
            Sounds.Initialize(_host);
        }

        private void OnApplicationQuit()
        {
            Speech.Shutdown();
        }

        private void OnDestroy()
        {
            // BepInEx may destroy its manager object; the controller lives on its own object.
        }

        internal static void LogInfo(string message) => Log?.LogInfo(message);
        internal static void LogWarning(string message) => Log?.LogWarning(message);
        internal static void LogError(string message) => Log?.LogError(message);

        internal static void LogDebug(string message)
        {
            if (ModConfig.DebugLogging != null && ModConfig.DebugLogging.Value)
                Log?.LogInfo("[debug] " + message);
        }
    }
}
