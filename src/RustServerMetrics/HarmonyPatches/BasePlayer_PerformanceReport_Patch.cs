using HarmonyLib;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace RustServerMetrics.HarmonyPatches
{
    [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.PerformanceReport))]
    public class BasePlayer_PerformanceReport_Patch
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> originalInstructions, ILGenerator iLGenerator)
        {
            var jmpLabel = iLGenerator.DefineLabel();
            List<CodeInstruction> retList = new List<CodeInstruction>(originalInstructions);

            var indexedMethodInfo = FindMethod(typeof(JsonConvert), nameof(JsonConvert.DeserializeObject), BindingFlags.Static | BindingFlags.Public, new Type[] { typeof(string) }, new Type[] { typeof(ClientPerformanceReport) });
            var insertionIndex = retList.FindIndex(x => x.opcode == OpCodes.Call && x.operand == indexedMethodInfo);
            if (insertionIndex < 0) throw new Exception("Failed to find the insertion index for BasePlayer_PerformanceReport_Patch");
            insertionIndex += 2;

            var fieldInfo = typeof(SingletonComponent<MetricsLogger>)
                .GetField(nameof(SingletonComponent<MetricsLogger>.Instance), BindingFlags.Static | BindingFlags.Public);

            var methodInfo = typeof(MetricsLogger)
                .GetMethod(nameof(MetricsLogger.OnClientPerformanceReport), BindingFlags.Instance | BindingFlags.NonPublic);

            var labels = retList[insertionIndex].labels;
            retList[insertionIndex].labels = new List<Label>();
            retList[retList.Count - 1].labels.Add(jmpLabel);

            retList.InsertRange(insertionIndex, new CodeInstruction[]
            {
                new CodeInstruction(OpCodes.Ldsfld, fieldInfo)
                {
                    labels = labels,
                },
                new CodeInstruction(OpCodes.Ldloc_2),
                new CodeInstruction(OpCodes.Call, methodInfo),
                new CodeInstruction(OpCodes.Brtrue, jmpLabel)
            });

            return retList;
        }

        static MethodInfo FindMethod(Type type, string name, BindingFlags bindingFlags, Type[] parameters, Type[] generics)
        {
            var methods = new List<MethodInfo>(type.GetMethods(bindingFlags));

            for (int i = methods.Count - 1; i >= 0; i--)
            {
                var method = methods[i];
                if (method.Name != name)
                {
                    methods.RemoveAt(i);
                    continue;
                }

                if (generics != null && generics.Length > 0)
                {
                    if (!method.ContainsGenericParameters)
                    {
                        methods.RemoveAt(i);
                        continue;
                    }

                    var methodGenerics = method.GetGenericArguments();

                    if (methodGenerics.Length != generics.Length)
                    {
                        methods.RemoveAt(i);
                        continue;
                    }

                    methods.RemoveAt(i);
                    methods.Insert(i, method.MakeGenericMethod(generics));
                }

                if (parameters != null && parameters.Length > 0)
                {
                    var methodParameters = method.GetParameters();
                    if (methodParameters.Length != parameters.Length)
                    {
                        methods.RemoveAt(i);
                        continue;
                    }

                    bool failedParameterMatch = false;
                    for (int j = 0; j < parameters.Length; j++)
                    {
                        var methodParameter = methodParameters[j];
                        var parameter = parameters[j];
                        if (methodParameter.ParameterType != parameter)
                        {
                            failedParameterMatch = true;
                            break;
                        }
                    };
                    if (failedParameterMatch)
                    {
                        methods.RemoveAt(i);
                        continue;
                    }
                }
            }

            if (methods.Count > 1)
            {
                throw new Exception("Matched multiple methods: " + string.Join("\n", methods.Select(x => x.ToString())));
            }

            if (methods.Count == 0)
            {
                throw new Exception("Found no matching methods");
            }

            return methods[0];
        }
    }
}