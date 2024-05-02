using HarmonyLib;
using RustServerMetrics.HarmonyPatches.Utility;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace RustServerMetrics.HarmonyPatches.Delayed
{
    [DelayedHarmonyPatch]
    [HarmonyPatch]
    internal class ConsoleSystem_Internal_Patch
    {
        [HarmonyPrepare]
        public static bool Prepare()
        {
            if (!RustServerMetricsLoader.__serverStarted)
            {
                Debug.Log("Note: Cannot patch ConsoleSystem_Internal_Patch yet. We will patch it upon server start.");
                return false;
            }

            return true;
        }
        
        [HarmonyTargetMethods]
        public static IEnumerable<MethodBase> TargetMethods(Harmony harmonyInstance)
        {
            yield return AccessTools.DeclaredMethod(typeof(ConsoleSystem), nameof(ConsoleSystem.Internal));
        }

        [HarmonyPrefix]
        public static void Prefix(ref DateTimeOffset __state)
        {
            __state = DateTimeOffset.UtcNow;
        }

        [HarmonyPostfix]
        public static void Postfix(ConsoleSystem.Arg arg, DateTimeOffset __state)
        {
            if (MetricsLogger.Instance == null)
                return;
            
            var duration = DateTimeOffset.UtcNow - __state;
            MetricsLogger.Instance.ServerConsoleCommands.LogTime(arg.cmd.FullName, duration.TotalMilliseconds);
        }
    }
}
