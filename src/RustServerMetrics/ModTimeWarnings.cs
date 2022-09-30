using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using RustServerMetrics.HarmonyPatches.Utility;

namespace RustServerMetrics;

public static class ModTimeWarnings
{
    public static List<MethodInfo> Methods = new ();

    [HarmonyTargetMethods]
    public static IEnumerable<MethodBase> TargetMethods(HarmonyInstance harmonyInstance) => Methods;
    
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
        MetricsLogger.Instance.TimeWarnings.LogTime(methodName, duration.TotalMilliseconds);
    }
}