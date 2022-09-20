using Harmony;
using RustServerMetrics.HarmonyPatches.Utility;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace RustServerMetrics.HarmonyPatches.Delayed
{
    [DelayedHarmonyPatch]
    internal class ConsoleSystem_Internal_Patch
    {
        [HarmonyTargetMethods]
        public static IEnumerable<MethodBase> TargetMethods(HarmonyInstance harmonyInstance)
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
            var duration = DateTimeOffset.UtcNow - __state;
            MetricsLogger.Instance?.OnConsoleCommand(arg, duration.TotalMilliseconds);
        }
    }
}
