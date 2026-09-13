using System;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;

using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;

namespace NetWasm.Compiler.Wasm.Emission;

internal sealed record ManagedMethodEmission(
    byte[] Body,
    FilterEnvironmentLayout FilterEnvironment,
    int WasmInstructionCount);

internal sealed class ManagedMethodEmitter(
    ITargetLayout layouts,
    IManagedMethodFunctionTypeResolver functionTypes,
    IValueFrameLayoutPlanner valueFrames,
    IFilterEnvironmentLayoutPlanner filterEnvironments,
    IExceptionGroupEnumerator exceptionGroups,
    IMethodFrameEntryEmitter frames,
    IMethodExceptionBoundaryEmitter exceptionBoundary,
    IGeneratedFunctionWriterFactory writers,
    IInstructionCountingWriterFactory instructionWriters) : IManagedMethodEmitter
{
    public ManagedMethodEmission Emit(
        MethodDefinitionModel method,
        ManagedMethodIdentity callerIdentity,
        StructuredMethod structured,
        MethodRootMap rootMap,
        MethodInstanceModel? methodInstance,
        int stackTraceMethodId,
        RuntimeImportSelection runtimeImportSelection,
        Action<IWasmInstructionWriter, StructuredMethod, MethodEmissionContext> emitBody)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(structured);
        ArgumentNullException.ThrowIfNull(rootMap);
        ArgumentNullException.ThrowIfNull(emitBody);

        var effectiveMethodInstance = methodInstance ?? structured.Header.MethodInstance;
        var effectiveSignature = effectiveMethodInstance?.Signature ?? method.Signature;
        var emissionStructure = structured with
        {
            Header = structured.Header with { MethodInstance = effectiveMethodInstance },
        };
        var valueLayout = valueFrames.Create(emissionStructure.Header);
        var filterEnvironment = filterEnvironments.Create(emissionStructure, valueLayout);
        valueLayout = valueLayout with
        {
            Size = Math.Max(valueLayout.Size, filterEnvironment.Size),
        };
        var parameterOffset = effectiveSignature.ReturnSignatureType.StackKind ==
            CliValueKind.ValueType ? 1 : 0;
        var parameterCount = effectiveMethodInstance is null
            ? functionTypes.Resolve(method).Parameters.Length
            : functionTypes.Resolve(effectiveMethodInstance).Parameters.Length;
        var groups = exceptionGroups.Enumerate(emissionStructure).ToArray();
        var emissionLayout = MethodEmissionContextFactory.Create(
            emissionStructure.Header,
            rootMap,
            parameterCount,
            parameterOffset,
            groups,
            valueLayout,
            filterEnvironment,
            callerIdentity);
        var context = emissionLayout.Context with
        {
            StackTraceMethodId = stackTraceMethodId,
            RuntimeImportSelection = runtimeImportSelection,
        };
        var output = writers.Create();
        var code = instructionWriters.Create(output.Instructions);
        WasmLocalDeclarationWriter.Write(
            output.Bytes,
            emissionLayout.LocalTypes,
            layouts.Target);
        frames.Emit(code, emissionStructure.Header, context);
        if (rootMap.SlotCount != 0 || valueLayout.Size != 0 ||
            stackTraceMethodId != 0 ||
            filterEnvironment.RootSlotCount != 0)
        {
            exceptionBoundary.Emit(
                code,
                context,
                protectedContext => emitBody(code, emissionStructure, protectedContext));
        }
        else
        {
            emitBody(code, emissionStructure, context);
        }
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Unreachable));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        return new ManagedMethodEmission(
            output.Snapshots.Read(),
            filterEnvironment,
            code.InstructionCount);
    }

}
