using System;
using System.Reflection;
using HarmonyLib;

namespace CultistAccessibility.Patches
{
    /// <summary>
    /// Manual patching by name (no [HarmonyPatch] attributes): a missing target logs a warning and the rest
    /// of the mod keeps working. Every target is listed in PATCH_TARGETS.md.
    /// </summary>
    internal static class PatchRegistry
    {
        public static int Applied;
        public static int Failed;

        public static void ApplyAll(Harmony harmony)
        {
            InputBlockPatches.TryPatch(harmony);
            NotificationPatches.TryPatch(harmony);
            SpeedPatches.TryPatch(harmony);
            CardPatches.TryPatch(harmony);
            Plugin.LogInfo("Patches applied: " + Applied + ", failed: " + Failed);
        }

        /// <summary>Patches typeName.methodName; parameter types disambiguate overloads.</summary>
        public static bool Patch(Harmony harmony, Type patchClass, string typeName, string methodName, Type[] args, string prefix = null, string postfix = null)
        {
            try
            {
                Type type = AccessTools.TypeByName(typeName);
                if (type == null)
                {
                    Plugin.LogWarning("Patch target type not found: " + typeName);
                    Failed++;
                    return false;
                }
                MethodInfo method = args == null ? AccessTools.Method(type, methodName) : AccessTools.Method(type, methodName, args);
                if (method == null)
                {
                    Plugin.LogWarning("Patch target not found: " + typeName + "." + methodName);
                    Failed++;
                    return false;
                }
                HarmonyMethod pre = prefix != null ? new HarmonyMethod(patchClass.GetMethod(prefix, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)) : null;
                HarmonyMethod post = postfix != null ? new HarmonyMethod(patchClass.GetMethod(postfix, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)) : null;
                harmony.Patch(method, pre, post);
                Plugin.LogInfo("Patched " + typeName + "." + methodName);
                Applied++;
                return true;
            }
            catch (Exception ex)
            {
                Plugin.LogError("Failed to patch " + typeName + "." + methodName + ": " + ex.Message);
                Failed++;
                return false;
            }
        }
    }
}
