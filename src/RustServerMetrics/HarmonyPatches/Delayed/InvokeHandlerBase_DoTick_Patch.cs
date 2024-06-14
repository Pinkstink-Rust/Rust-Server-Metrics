using HarmonyLib;
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
    [HarmonyPatch]
    internal static class InvokeHandlerBase_DoTick_Patch
    {
        static System.Diagnostics.Stopwatch _stopwatch = new System.Diagnostics.Stopwatch();
        static bool _failedExecution = false;

        readonly static CodeInstruction[] _replacementSequenceToFind = new CodeInstruction[]
        {
            new CodeInstruction(OpCodes.Ldloc_S),
            new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(InvokeAction), nameof(InvokeAction.action))),
            new CodeInstruction(OpCodes.Callvirt, AccessTools.Method(typeof(Action), nameof(Action.Invoke))),
            new CodeInstruction(OpCodes.Br_S)
        };

        readonly static CodeInstruction[] _jmpSequenceToFind = new CodeInstruction[]
{
            new CodeInstruction(OpCodes.Ldloc_S),
            new CodeInstruction(OpCodes.Ldc_I4_1),
            new CodeInstruction(OpCodes.Add)
};

        [HarmonyPrepare]
        public static bool Prepare()
        {
            if (!RustServerMetricsLoader.__serverStarted)
            {
                Debug.Log("Note: Cannot patch InvokeHandlerBase_DoTick_Patch yet. We will patch it upon server start.");
                return false;
            }

            return true;
        }
        
        [HarmonyTargetMethods]
        public static IEnumerable<MethodBase> TargetMethods(Harmony harmonyInstance)
        {
            yield return AccessTools.DeclaredMethod(typeof(InvokeHandlerBase<InvokeHandler>), nameof(InvokeHandlerBase<InvokeHandler>.DoTick));
        }

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> originalInstructions, MethodBase methodBase)
        {
            LocalVariableInfo variableInfo = methodBase.GetMethodBody().LocalVariables.FirstOrDefault(x => x.LocalType == typeof(InvokeAction));
            _replacementSequenceToFind[0].operand = variableInfo;
            _jmpSequenceToFind[0].operand = variableInfo;

            var instructionsList = new List<CodeInstruction>(originalInstructions);

            var jmpIdx = GetSequenceStartIndex(instructionsList, _jmpSequenceToFind);
            if (jmpIdx < 0) throw new Exception($"Failed to find jmp injection index for {nameof(InvokeHandlerBase_DoTick_Patch)}");

            _replacementSequenceToFind[3].operand = instructionsList[jmpIdx];

            var methodToCallInfo = typeof(InvokeHandlerBase_DoTick_Patch)
                .GetMethod(nameof(InvokeWrapper), BindingFlags.Static | BindingFlags.NonPublic);

            var replacementIdx = GetSequenceStartIndex(instructionsList, _replacementSequenceToFind);
            if (replacementIdx < 0) throw new Exception($"Failed to find replacement injection index for {nameof(InvokeHandlerBase_DoTick_Patch)}");

            instructionsList.RemoveRange(replacementIdx + 1, _replacementSequenceToFind.Length - 2);
            instructionsList.InsertRange(replacementIdx + 1, new CodeInstruction[]
            {
                new CodeInstruction(OpCodes.Call, methodToCallInfo)
            });

            return instructionsList;
        }

        static void InvokeWrapper(InvokeAction invokeAction)
        {
            _stopwatch.Restart();
            _failedExecution = false;
            try
            {
                invokeAction.action.Invoke();
            }
            catch (Exception)
            {
                _failedExecution = true;
                throw;
            }
            finally
            {
                _stopwatch.Stop();
                MetricsLogger.Instance?.ServerInvokes.LogTime(invokeAction.action.Method, _stopwatch.Elapsed.TotalMilliseconds);
            }
        }

        static int GetSequenceStartIndex(List<CodeInstruction> originalList, CodeInstruction[] sequenceToFind, bool debug = false)
        {
            CodeInstruction firstSequence = sequenceToFind[0];
            for (int i = 0; i < originalList.Count; i++)
            {
                if (originalList.Count - i < sequenceToFind.Length)
                    break;

                var instruction = originalList[i];

                if (debug && instruction.opcode == firstSequence.opcode)
                {
                    UnityEngine.Debug.Log($"Trying to match starting sequence {i}, {instruction.opcode} <-> {firstSequence.opcode}, ({instruction.operand?.GetType().FullName ?? "null"}){instruction.operand} <-> ({firstSequence.operand?.GetType().FullName ?? "null"}){firstSequence.operand}");
                }
                if (instruction.opcode == firstSequence.opcode)
                {
                    switch (instruction.operand)
                    {
                        case LocalBuilder:
                        case LocalVariableInfo:
                            var instructionAsLocalVarInfo = instruction.operand as LocalVariableInfo;
                            var firstSequenceAsLocalVarInfo = firstSequence.operand as LocalVariableInfo;
                            if (instructionAsLocalVarInfo.LocalType != firstSequenceAsLocalVarInfo.LocalType || instructionAsLocalVarInfo.LocalIndex != firstSequenceAsLocalVarInfo.LocalIndex) continue;
                            break;

                        default:
                            if (instruction.operand != firstSequence.operand) continue;
                            break;
                    }

                    bool found = true;
                    int z;
                    for (z = 1; z < sequenceToFind.Length; z++)
                    {
                        var currentInstruction = originalList[i + z];
                        var sequenceInstruction = sequenceToFind[z];
                        if (currentInstruction.opcode != sequenceInstruction.opcode)
                        {
                            if (sequenceInstruction.operand != null && currentInstruction.operand != sequenceInstruction.operand)
                            {
                                if (debug)
                                {
                                    UnityEngine.Debug.Log($"Failed match {z}, {currentInstruction.opcode} <-> {sequenceInstruction.opcode}, ({currentInstruction.operand?.GetType().FullName ?? "null"}){currentInstruction.operand} <-> ({sequenceInstruction.operand?.GetType().FullName ?? "null"}){sequenceInstruction.operand}");
                                }
                                found = false;
                                break;
                            }
                        }
                    }

                    if (found) return i;
                }
            }

            return -1;
        }
    }
}
