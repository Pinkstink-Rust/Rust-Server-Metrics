using Harmony;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;

namespace RustServerMetrics.Harmony.Bootstrap
{
    [HarmonyPatch(typeof(global::Bootstrap), nameof(global::Bootstrap.StartServer))]
    public class StartServer_Patch
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> originalInstructions)
        {
            List<CodeInstruction> retList = new List<CodeInstruction>(originalInstructions);

            var methodInfo = typeof(MetricsLogger)
                .GetMethod(nameof(MetricsLogger.Initialize), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

            retList.InsertRange(0, new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Call, methodInfo)
            });

            return retList;
        }
    }
}
