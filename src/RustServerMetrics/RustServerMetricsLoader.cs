using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RustServerMetrics;

public class RustServerMetricsLoader : IHarmonyModHooks
{
    public static bool __serverStarted = false;
    
    public static Harmony __harmonyInstance;
    public static List<Harmony> __modTimeWarningsHarmonyInstances = new ();
    
    public void OnLoaded(OnHarmonyModLoadedArgs args)
    {
        if (!Bootstrap.bootstrapInitRun)
            return;
        
        MetricsLogger.Initialize();
            
        if (MetricsLogger.Instance != null)
            MetricsLogger.Instance.OnServerStarted();
    }

    public void OnUnloaded(OnHarmonyModUnloadedArgs args)
    {
        __harmonyInstance?.UnpatchAll();
        foreach (var instance in __modTimeWarningsHarmonyInstances)
        {
            instance?.UnpatchAll();
        }
        
        if (MetricsLogger.Instance != null)
            Object.DestroyImmediate(MetricsLogger.Instance);
    }

    public void AddModTimeWarnings(List<MethodInfo> methods)
    { 
        var instance = new Harmony($"RustServerMetrics.ModTimeWarnings.{__modTimeWarningsHarmonyInstances.Count}");
        __modTimeWarningsHarmonyInstances.Add(instance);
         
        ModTimeWarnings.Methods.Clear();
        ModTimeWarnings.Methods.AddRange(methods);
        
        var patchProcessor = new PatchClassProcessor(instance, typeof(ModTimeWarnings));
        patchProcessor.Patch();
        
        foreach (var method in methods)
        {
            Debug.Log($"{method.DeclaringType?.Name}.{method.Name}");
        }

        Debug.Log($"[ServerMetrics]: Added {methods.Count} ModTimeWarnings");
    }
}