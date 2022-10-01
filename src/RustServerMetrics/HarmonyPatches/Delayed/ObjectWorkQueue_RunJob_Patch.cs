using Harmony;
using RustServerMetrics.HarmonyPatches.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace RustServerMetrics.HarmonyPatches.Delayed
{
    [DelayedHarmonyPatch]
    internal static class ObjectWorkQueue_RunJob_Patch
    {
        [HarmonyTargetMethods]
        public static IEnumerable<MethodBase> TargetMethods(HarmonyInstance harmonyInstance)
        {
            var assemblyCSharp = typeof(BaseNetworkable).Assembly;
            Stack<Type> typesToScan = new Stack<Type>(assemblyCSharp.GetTypes());
            HashSet<string> yielded = new ();
            
            while (typesToScan.TryPop(out Type type))
            {
                var subTypes = type.GetNestedTypes();
                foreach (var t in subTypes)
                    typesToScan.Push(t);

                if (type.BaseType == null || !type.BaseType.Name.Contains("ObjectWorkQueue"))
                    continue;

                if (!yielded.Contains(type.FullName))
                {
                    yielded.Add(type.FullName);
                    yield return AccessTools.Method(type, "RunJob");
                }
            }
        }

        
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> originalInstructions, MethodBase methodBase, ILGenerator ilGenerator)
        {
            List<CodeInstruction> ret = originalInstructions.ToList();
            LocalBuilder local = ilGenerator.DeclareLocal(typeof(DateTime));
            
            ret.InsertRange(0, new CodeInstruction []
            { 
                new (OpCodes.Call, AccessTools.Property(typeof(DateTime), nameof(DateTime.UtcNow)).GetMethod),
                new (OpCodes.Stloc, local)
            });

            return Helpers.Postfix(
                ret,
                CustomPostfix, 
                new CodeInstruction(OpCodes.Ldstr, $"{methodBase.DeclaringType?.Name}.{methodBase.Name}"),
                new CodeInstruction(OpCodes.Ldloc, local));
        }
         

        public static void CustomPostfix(string methodName, DateTime __state)
        {
            if (MetricsLogger.Instance == null)
                return;
            
            var duration = DateTime.UtcNow - __state;
            MetricsLogger.Instance.WorkQueueTimes.LogTime(methodName, duration.TotalMilliseconds);
        }
    }
}
