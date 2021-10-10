using Harmony;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace RustServerMetrics.HarmonyPatches
{
    [HarmonyPatch]
    public static class OxideMod_OnFrame_Patch
    {
        const string OxideCore_AssemblyName = "Oxide.Core";

        const string OxidePluginType_FullName = "Oxide.Core.Plugins.Plugin";
        const string OxideInterfaceType_FullName = "Oxide.Core.Interface";
        const string OxideOxideModType_FullName = "Oxide.Core.OxideMod";
        const string OxidePluginManagerType_FullName = "Oxide.Core.Plugins.PluginManager";

        static Assembly _oxideCoreAssembly = null;
        public static float _tickAccumulator = 0f;

        [HarmonyTargetMethods]
        public static IEnumerable<MethodBase> TargetMethods(HarmonyInstance harmonyInstance)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                var assembly = assemblies[i];
                if (!string.Equals(assembly.GetName().Name, OxideCore_AssemblyName, StringComparison.OrdinalIgnoreCase)) continue;
                _oxideCoreAssembly = assembly;
                break;
            }

            if (_oxideCoreAssembly == null)
                return Array.Empty<MethodBase>();

            var oxideModType = _oxideCoreAssembly.GetType(OxideOxideModType_FullName, false);

            if (oxideModType == null)
                return Array.Empty<MethodBase>();

            var targetMethod = oxideModType.GetMethod("OnFrame", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { typeof(float) }, null);

            if (targetMethod == null)
                return Array.Empty<MethodBase>();

            return new MethodBase[] { targetMethod };
        }

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> originalInstructions, ILGenerator ilGenerator)
        {
            var skipProcessingLabel = ilGenerator.DefineLabel();
            var loopHeadLabel = ilGenerator.DefineLabel();
            var loopBodyLabel = ilGenerator.DefineLabel();

            var oxidePluginType = _oxideCoreAssembly.GetType(OxidePluginType_FullName, true);
            var enumerableType = typeof(IEnumerable<>);
            var oxidePluginEnumerableType = enumerableType.MakeGenericType(oxidePluginType);
            var enumeratorType = typeof(IEnumerator<>);
            var oxidePluginEnumeratorType = enumeratorType.MakeGenericType(oxidePluginType);

            var enumeratorLocal = ilGenerator.DeclareLocal(oxidePluginEnumeratorType);
            var dictionaryLocal = ilGenerator.DeclareLocal(typeof(Dictionary<string, double>));
            var pluginLocal = ilGenerator.DeclareLocal(oxidePluginType);

            var tickAccumulatorFieldInfo = typeof(OxideMod_OnFrame_Patch).GetField(nameof(_tickAccumulator), BindingFlags.Public | BindingFlags.Static);

            List<CodeInstruction> retList = new List<CodeInstruction>(originalInstructions);

            retList[0].labels.Add(skipProcessingLabel);

            var oxideGetterMethodInfo = _oxideCoreAssembly.GetType(OxideInterfaceType_FullName, true).GetProperty("Oxide").GetGetMethod();
            var rootPluginManagerGetterMethodInfo = _oxideCoreAssembly.GetType(OxideOxideModType_FullName, true).GetProperty("RootPluginManager").GetGetMethod();
            var getPluginsMethodInfo = _oxideCoreAssembly.GetType(OxidePluginManagerType_FullName, true).GetMethod("GetPlugins");
            var getPluginNameGetterMethodInfo = oxidePluginType.GetProperty("Name").GetGetMethod();
            var getPluginTotalHookTimeGetterMethodInfo = oxidePluginType.GetProperty("TotalHookTime").GetGetMethod();
            var getEnumeratorMethodInfo = oxidePluginEnumerableType.GetMethod("GetEnumerator");

            var hookFieldInfo = typeof(SingletonComponent<MetricsLogger>)
                .GetField(nameof(SingletonComponent<MetricsLogger>.Instance), BindingFlags.Static | BindingFlags.Public);

            var hookMethodInfo = typeof(MetricsLogger)
                .GetMethod(nameof(MetricsLogger.OnOxidePluginMetrics), BindingFlags.Instance | BindingFlags.NonPublic);

            retList.InsertRange(0, new CodeInstruction[]
            {
                new CodeInstruction(OpCodes.Ldsfld, hookFieldInfo),
                new CodeInstruction(OpCodes.Brfalse_S, skipProcessingLabel),

                new CodeInstruction(OpCodes.Ldsfld, tickAccumulatorFieldInfo),
                new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Add),
                new CodeInstruction(OpCodes.Stsfld, tickAccumulatorFieldInfo),
                new CodeInstruction(OpCodes.Ldsfld, tickAccumulatorFieldInfo),
                new CodeInstruction(OpCodes.Ldc_R4, 1),
                new CodeInstruction(OpCodes.Blt_Un_S, skipProcessingLabel),

                new CodeInstruction(OpCodes.Ldsfld, tickAccumulatorFieldInfo),
                new CodeInstruction(OpCodes.Ldc_R4, 1),
                new CodeInstruction(OpCodes.Sub),
                new CodeInstruction(OpCodes.Stsfld, tickAccumulatorFieldInfo),

                new CodeInstruction(OpCodes.Call, oxideGetterMethodInfo),
                new CodeInstruction(OpCodes.Callvirt, rootPluginManagerGetterMethodInfo),
                new CodeInstruction(OpCodes.Callvirt, getPluginsMethodInfo),
                new CodeInstruction(OpCodes.Callvirt, getEnumeratorMethodInfo),
                new CodeInstruction(OpCodes.Stloc, enumeratorLocal.LocalIndex),

                new CodeInstruction(OpCodes.Newobj, typeof(Dictionary<string, double>).GetConstructor(Type.EmptyTypes)),
                new CodeInstruction(OpCodes.Stloc, dictionaryLocal),

                // Jump to Loop Head
                new CodeInstruction(OpCodes.Br_S, loopHeadLabel),

                // Loop Body Start
                new CodeInstruction(OpCodes.Ldloc, enumeratorLocal.LocalIndex) { labels = { loopBodyLabel } },
                new CodeInstruction(OpCodes.Callvirt, oxidePluginEnumeratorType.GetProperty(nameof(IEnumerator.Current)).GetGetMethod()),
                new CodeInstruction(OpCodes.Stloc, pluginLocal.LocalIndex),
                new CodeInstruction(OpCodes.Ldloc, dictionaryLocal.LocalIndex),
                new CodeInstruction(OpCodes.Ldloc, pluginLocal.LocalIndex),
                new CodeInstruction(OpCodes.Callvirt, getPluginNameGetterMethodInfo),
                new CodeInstruction(OpCodes.Ldloc, pluginLocal.LocalIndex),
                new CodeInstruction(OpCodes.Callvirt, getPluginTotalHookTimeGetterMethodInfo),
                new CodeInstruction(OpCodes.Callvirt, dictionaryLocal.LocalType.GetMethod("set_Item")),
                // Loop Body End

                // Loop Head Start
                new CodeInstruction(OpCodes.Ldloc, enumeratorLocal.LocalIndex) { labels = { loopHeadLabel } },
                new CodeInstruction(OpCodes.Callvirt, typeof(IEnumerator).GetMethod(nameof(IEnumerator.MoveNext))),
                new CodeInstruction(OpCodes.Brtrue_S, loopBodyLabel),
                // Loop Head End
                
                // Dispose of IEnumerator
                new CodeInstruction(OpCodes.Ldloc, enumeratorLocal.LocalIndex),
                new CodeInstruction(OpCodes.Callvirt, typeof(IDisposable).GetMethod(nameof(IDisposable.Dispose))),

                // Call MetricsLogger.OnOxidePluginMetrics
                new CodeInstruction(OpCodes.Ldsfld, hookFieldInfo),
                new CodeInstruction(OpCodes.Ldloc, dictionaryLocal.LocalIndex),
                new CodeInstruction(OpCodes.Call, hookMethodInfo)
            });

            return retList;
        }
    }
}