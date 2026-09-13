using System;
using System.Diagnostics;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Planning;

using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed class ManagedMethodBodyEmitter(
    IManagedMethodEmitter managedMethods,
    IManagedMethodSequenceEmitter sequences,
    IStackTraceMethodIdProvider stackTraceMethodIds) : IManagedMethodBodyEmitter
{
    public ManagedMethodBodyEmission Emit(
        MethodDefinitionModel method,
        ManagedMethodIdentity callerIdentity,
        StructuredMethod structured,
        MethodRootMap rootMap,
        InstructionModuleTarget target,
        IFunctionIndexResolver functionIndices,
        MethodInstanceModel? methodInstance = null)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(structured);
        ArgumentNullException.ThrowIfNull(rootMap);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(functionIndices);

        var started = Stopwatch.GetTimestamp();
        var memoryBefore = GC.GetTotalMemory(forceFullCollection: false);
        ManagedMethodSequenceEmission? sequenceEmission = null;
        var stackTraceMethodId = stackTraceMethodIds.GetId(
            target.StackTraceMethods,
            method,
            methodInstance);
        var emission = managedMethods.Emit(
            method,
            callerIdentity,
            structured,
            rootMap,
            methodInstance,
            stackTraceMethodId,
            target.RuntimeImportSelection,
            (code, emittedMethod, context) =>
                sequenceEmission = sequences.Emit(
                    code,
                    emittedMethod,
                    emittedMethod.Body,
                    context,
                    target,
                    functionIndices));
        return new ManagedMethodBodyEmission(
            emission.Body,
            emission.WasmInstructionCount,
            Stopwatch.GetElapsedTime(started).Ticks,
            Math.Max(memoryBefore, GC.GetTotalMemory(forceFullCollection: false)),
            ManagedMethodBodyKey.Resolve(structured),
            emission.FilterEnvironment,
            sequenceEmission?.OriginalBlockEmissionCounts ?? []);
    }

}
