using Harmony;
using System.Collections.Generic;
using System.Reflection.Emit;

namespace RustServerMetrics.Harmony.NetWrite
{
    [HarmonyPatch(typeof(Network.NetWrite), nameof(Network.NetWrite.Send))]
    public class Send_Patch
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> originalInstructions)
        {
            List<CodeInstruction> retList = new List<CodeInstruction>(originalInstructions);

            var fieldInfo = typeof(SingletonComponent<MetricsLogger>)
                .GetField(nameof(SingletonComponent<MetricsLogger>.Instance), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);

            var methodInfo = typeof(MetricsLogger)
                .GetMethod(nameof(MetricsLogger.OnNetWriteSend), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            retList.InsertRange(retList.Count - 1, new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldsfld, fieldInfo),
                new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Call, methodInfo)
            });

            return retList;
        }
    }
}