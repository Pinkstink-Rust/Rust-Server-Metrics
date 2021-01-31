using Harmony;
using Network;
using Oxide.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;

namespace RustServerMetrics_HarmonyPatch.NetWrite
{
    [HarmonyPatch(typeof(Network.NetWrite), nameof(Network.NetWrite.Send))]
    public class Send_Patch
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> originalInstructions)
        {
            List<CodeInstruction> retList = new List<CodeInstruction>(originalInstructions);

            var methodInfo = typeof(Interface).GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public).FirstOrDefault(x => x.Name == "CallHook" && x.GetParameters().Count() == 2 && x.GetParameters()[1].ParameterType == typeof(object));

            retList.InsertRange(retList.Count - 1, new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldstr, "OnNetWriteSend"),
                new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Box, typeof(SendInfo)),
                new CodeInstruction(OpCodes.Call, methodInfo),
                new CodeInstruction(OpCodes.Pop)
            });

            return retList;
        }
    }
}