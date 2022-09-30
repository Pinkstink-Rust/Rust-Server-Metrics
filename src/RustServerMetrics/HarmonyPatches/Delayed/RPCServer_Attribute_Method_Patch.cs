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
    internal class RPCServer_Attribute_Method_Patch
    {
        [HarmonyTargetMethods]
        public static IEnumerable<MethodBase> TargetMethods(HarmonyInstance harmonyInstance)
        {
            var baseNetworkableType = typeof(BaseNetworkable);
            var baseNetworkableAssembly = baseNetworkableType.Assembly;
            Stack<Type> typesToScan = new Stack<Type>(baseNetworkableAssembly.GetTypes());

            while (typesToScan.TryPop(out Type type))
            {
                var subTypes = type.GetNestedTypes();
                for (int i = 0; i < subTypes.Length; i++)
                    typesToScan.Push(subTypes[i]);

                var methods = type.GetMethods();
                for (int i = 0; i < methods.Length; i++)
                {
                    var method = methods[i];
                    if (method.DeclaringType == method.ReflectedType && method.GetCustomAttribute<BaseEntity.RPC_Server>() != null)
                    {
                        yield return method;
                    }
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
            MetricsLogger.Instance.ServerRpcCalls.LogTime(methodName, duration.TotalMilliseconds);
        }
    }
}
