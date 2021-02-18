using Harmony;
using System.Collections.Generic;
using System.Reflection.Emit;

namespace RustServerMetrics.Harmony.TimeWarning
{
    [HarmonyPatch(typeof(global::TimeWarning), nameof(global::TimeWarning.New))]
    public static class New_Patch
    {
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
