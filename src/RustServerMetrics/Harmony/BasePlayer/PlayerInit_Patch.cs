using Harmony;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace RustServerMetrics.Harmony.BasePlayer
{
    [HarmonyPatch(typeof(global::BasePlayer), nameof(global::BasePlayer.PlayerInit))]
    public class PlayerInit_Patch
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> originalInstructions)
        {
            List<CodeInstruction> retList = new List<CodeInstruction>(originalInstructions);

            var fieldInfo = typeof(SingletonComponent<MetricsLogger>)
                .GetField(nameof(SingletonComponent<MetricsLogger>.Instance), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);

            var methodInfo = typeof(MetricsLogger)
                .GetMethod(nameof(MetricsLogger.OnPlayerInit), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            var idx = retList.FindIndex(x => x.opcode == OpCodes.Call && x.operand is MethodInfo methodInfo1 && methodInfo1.DeclaringType.Name == "EACServer" && methodInfo1.Name == "OnStartLoading");

            if (idx < 0) throw new Exception("Failed to find the insertion index for PlayerInit");

            retList.InsertRange(idx, new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldsfld, fieldInfo),
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Call, methodInfo)
            });

            return retList;
        }
    }
}