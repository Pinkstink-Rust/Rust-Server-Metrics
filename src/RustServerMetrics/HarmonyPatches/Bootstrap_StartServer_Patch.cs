using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace RustServerMetrics.HarmonyPatches
{
    [HarmonyPatch(typeof(Bootstrap), nameof(Bootstrap.StartServer))]
    public class Bootstrap_StartServer_Patch
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> originalInstructions)
        {
            List<CodeInstruction> retList = new List<CodeInstruction>(originalInstructions);

            var methodInfo = typeof(MetricsLogger)
                .GetMethod(nameof(MetricsLogger.Initialize), BindingFlags.Static | BindingFlags.NonPublic);

            retList.InsertRange(0, new CodeInstruction[]
            {
                new CodeInstruction(OpCodes.Call, methodInfo)
            });

            return retList;
        }
    }
}
