using HarmonyLib;
using RustServerMetrics.HarmonyPatches.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace RustServerMetrics.HarmonyPatches.Delayed;

[DelayedHarmonyPatch]
[HarmonyPatch]
internal class RPCServer_Attribute_Method_Patch
{
    [HarmonyPrepare]
    public static bool Prepare()
    {
        if (RustServerMetricsLoader.__serverStarted)
        {
            return true;
        }
            
        Debug.Log("Note: Cannot patch RPCServer_Attribute_Method_Patch yet. We will patch it upon server start.");
        return false;
    }
        
    [HarmonyTargetMethods]
    public static IEnumerable<MethodBase> TargetMethods(Harmony harmonyInstance)
    {
        var baseNetworkableType = typeof(BaseNetworkable);
        var baseNetworkableAssembly = baseNetworkableType.Assembly;
        var typesToScan = new Stack<Type>(baseNetworkableAssembly.GetTypes());

        while (typesToScan.TryPop(out var type))
        {
            foreach (var subType in type.GetNestedTypes())
            {
                typesToScan.Push(subType);
            }
                
            foreach (var method in type.GetMethods())
            {
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
        var ret = originalInstructions.ToList();
        var local = ilGenerator.DeclareLocal(typeof(DateTime));
            
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
        {
            return;
        }
            
        var duration = DateTime.UtcNow - __state;
        MetricsLogger.Instance.ServerRpcCalls.LogTime(methodName, duration.TotalMilliseconds);
    }
}