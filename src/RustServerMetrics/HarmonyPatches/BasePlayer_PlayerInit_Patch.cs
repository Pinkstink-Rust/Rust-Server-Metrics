using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace RustServerMetrics.HarmonyPatches
{
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.PlayerInit))]
    public class BasePlayer_PlayerInit_Patch
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> originalInstructions)
        {
            List<CodeInstruction> retList = new List<CodeInstruction>(originalInstructions);

            var fieldInfo = typeof(SingletonComponent<MetricsLogger>)
                .GetField(nameof(SingletonComponent<MetricsLogger>.Instance), BindingFlags.Static | BindingFlags.Public);

            var methodInfo = typeof(MetricsLogger)
                .GetMethod(nameof(MetricsLogger.OnPlayerInit), BindingFlags.Instance | BindingFlags.NonPublic);

            var idx = retList.FindIndex(x => x.opcode == OpCodes.Call && x.operand is MethodInfo methodInfo1 && methodInfo1.DeclaringType.Name == "EACServer" && methodInfo1.Name == "OnStartLoading");

            if (idx < 0) throw new Exception("Failed to find the insertion index for PlayerInit");

            retList.InsertRange(idx, new CodeInstruction[]
            {
                new CodeInstruction(OpCodes.Ldsfld, fieldInfo),
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Call, methodInfo)
            });

            return retList;
        }
    }
}