using Harmony;
using Oxide.Core;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;

namespace RustServerMetrics_HarmonyPatch.Performance
{
    [HarmonyPatch(typeof(global::Performance), "FPSTimer")]
    public class FPSTimer_Patch
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> originalInstructions)
        {
            List<CodeInstruction> retList = new List<CodeInstruction>(originalInstructions);

            var lastIdxIsRet = retList.ElementAt(retList.Count - 1).opcode == OpCodes.Ret;
            var insertIdx = retList.Count - (lastIdxIsRet ? 2 : 1);

            var methodInfo = typeof(Interface).GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public).FirstOrDefault(x => x.Name == "CallHook" && x.GetParameters().Count() == 1);

            retList.InsertRange(insertIdx, new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldstr, "OnPerformanceReportGenerated"),
                new CodeInstruction(OpCodes.Call, methodInfo),
                new CodeInstruction(OpCodes.Pop)
            });

            return retList;
        }
    }
}
