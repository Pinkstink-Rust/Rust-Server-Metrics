using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace RustServerMetrics.HarmonyPatches
{
    [HarmonyPatch(typeof(TimeWarning), nameof(TimeWarning.New))]
    public static class TimeWarning_New_Patch
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> originalInstructions)
        {
            var methodInfo = typeof(MetricsTimeWarning)
                .GetMethod(nameof(MetricsTimeWarning.GetTimeWarning), BindingFlags.Static | BindingFlags.Public);

            return new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Call, methodInfo),
                new CodeInstruction(OpCodes.Ret)
            };
        }
    }
}
