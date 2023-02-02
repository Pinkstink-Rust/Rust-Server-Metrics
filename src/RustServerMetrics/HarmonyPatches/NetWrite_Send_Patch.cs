using Harmony;
using Network;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace RustServerMetrics.HarmonyPatches
{
    [HarmonyPatch(typeof(NetWrite), nameof(NetWrite.Send))]
    public class NetWrite_Send_Patch
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> originalInstructions)
        {
            List<CodeInstruction> retList = new List<CodeInstruction>(originalInstructions);

            var fieldInfo = typeof(SingletonComponent<MetricsLogger>)
                .GetField(nameof(SingletonComponent<MetricsLogger>.Instance), BindingFlags.Static | BindingFlags.Public);

            var methodInfo = typeof(MetricsLogger)
                .GetMethod(nameof(MetricsLogger.OnNetWriteSend), BindingFlags.Instance | BindingFlags.NonPublic);

            retList.InsertRange(retList.Count - 1, new CodeInstruction[]
            {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldsfld, fieldInfo),
                new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Call, methodInfo)
            });

            return retList;
        }
    }
}