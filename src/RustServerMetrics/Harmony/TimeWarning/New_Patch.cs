using Harmony;
using System.Collections.Generic;
using System.Reflection.Emit;

namespace RustServerMetrics.Harmony.TimeWarning
{
    [HarmonyPatch(typeof(global::TimeWarning), nameof(global::TimeWarning.New))]
    public static class New_Patch
    {
        /// <summary>
        /// Disabled as this patch doesn't apply with Harmony v1, will enable with Harmony v2 upgrade
        /// </summary>
        [HarmonyPrepare]
        public static bool Prepare(HarmonyInstance harmonyInstance) => false;

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> originalInstructions)
        {
            var fieldInfo = typeof(SingletonComponent<MetricsLogger>)
                .GetField(nameof(SingletonComponent<MetricsLogger>.Instance), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);

            var methodInfo = typeof(MetricsLogger)
                .GetMethod(nameof(MetricsLogger.OnNewTimeWarning), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            return new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldsfld, fieldInfo),
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Call, methodInfo),
                new CodeInstruction(OpCodes.Ret)
            };
        }
    }
}
