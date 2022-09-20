using Harmony;
using System;

namespace RustServerMetrics.HarmonyPatches
{
    [HarmonyPatch(typeof(ConsoleSystem), nameof(ConsoleSystem.Internal))]
    public class ConsoleSystem_Internal_Patch
    {
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
