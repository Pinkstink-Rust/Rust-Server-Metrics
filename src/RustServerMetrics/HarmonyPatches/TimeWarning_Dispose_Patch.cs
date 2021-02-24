using Harmony;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace RustServerMetrics.HarmonyPatches
{
    //[HarmonyPatch(typeof(TimeWarning), nameof(TimeWarning.Dispose))]
    public static class TimeWarning_Dispose_Patch
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> originalInstructions)
        {
            var methodInfo = typeof(MetricsTimeWarning)
                .GetMethod(nameof(MetricsTimeWarning.DisposeTimeWarning), BindingFlags.Static | BindingFlags.Public);

            return new CodeInstruction[]
            {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Call, methodInfo),
                new CodeInstruction(OpCodes.Ret)
            };
        }
    }
}
