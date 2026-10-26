using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Instructions.Interop;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal interface IJSImportForwarderFunctionEmitter
{
    byte[] Emit(
        MethodInstanceModel import,
        InstructionModuleTarget target,
        IFunctionIndexResolver functionIndices);
}

// Gives a delegate-bound JSImport a managed-ABI body: its parameters become the
// evaluation stack of one JSImport invocation, whose marshalled result is returned.
internal sealed class JSImportForwarderFunctionEmitter(
    ITargetLayout layouts,
    IJSImportInvocationEmitter invocations,
    IGeneratedFunctionWriterFactory writers) : IJSImportForwarderFunctionEmitter
{
    private static readonly CliValueKind[] StackGroups =
    [
        CliValueKind.I4,
        CliValueKind.I8,
        CliValueKind.F4,
        CliValueKind.F8,
        CliValueKind.ManagedReference,
        CliValueKind.ManagedAddress,
    ];

    public byte[] Emit(
        MethodInstanceModel import,
        InstructionModuleTarget target,
        IFunctionIndexResolver functionIndices)
    {
        ArgumentNullException.ThrowIfNull(import);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(functionIndices);

        var parameters = import.Signature.ParameterTypes;
        var slotCount = Math.Max(parameters.Length, 1);
        var stackLocals = WasmLocalLayoutPlanner.CreateEvaluationStack(parameters.Length, slotCount);
        var objectTemporary = stackLocals.End;
        var rootFrame = checked(objectTemporary + 1);
        var exceptionTemporary = checked(rootFrame + 1);
        var valueFrame = checked(exceptionTemporary + 1);
        var interopDescriptor = checked(valueFrame + 1);
        var interopResult = checked(interopDescriptor + 1);
        var interopHandle = checked(interopResult + 1);
        var numericTemporaryI4 = checked(interopHandle + 1);
        var numericTemporaryI8 = checked(numericTemporaryI4 + 1);
        var dispatcherProgramCounter = checked(numericTemporaryI8 + 1);
        ImmutableArray<CliValueKind> localTypes =
        [
            .. StackGroups.SelectMany(group => Enumerable.Repeat(group, slotCount)),
            CliValueKind.ManagedReference,
            CliValueKind.ManagedAddress,
            CliValueKind.ManagedReference,
            CliValueKind.ManagedAddress,
            CliValueKind.ManagedAddress,
            CliValueKind.I4,
            CliValueKind.I4,
            CliValueKind.I4,
            CliValueKind.I8,
            CliValueKind.I4,
            CliValueKind.I4,
            CliValueKind.I4,
            CliValueKind.I4,
        ];
        var context = new MethodEmissionContext(
            new MethodRootMap(import.Definition.Key, [], []),
            parameters.Length,
            parameters.Length,
            stackLocals,
            objectTemporary,
            rootFrame,
            exceptionTemporary,
            ImmutableDictionary<StructuredExceptionGroupId, int>.Empty,
            ImmutableDictionary<StructuredExceptionGroupId, int>.Empty,
            valueFrame,
            new ValueFrameLayout(0, [], [], [], []),
            rootFrame,
            FilterEnvironmentLayout.Empty,
            0,
            interopDescriptor,
            interopResult,
            interopHandle,
            numericTemporaryI4,
            numericTemporaryI8,
            dispatcherProgramCounter,
            RuntimeImportSelection: target.RuntimeImportSelection)
        {
            ManagedThrowFunction = target.FunctionIndices.ManagedThrowFunctionIndex,
        };

        var output = writers.Create();
        var code = output.Instructions;
        WasmLocalDeclarationWriter.Write(output.Bytes, localTypes, layouts.Target);
        var stack = new List<CliValueKind>(parameters.Length);
        for (var index = 0; index < parameters.Length; index++)
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned(checked((uint)index))));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalSet,
                WasmInstructionOperand.Unsigned(checked((uint)WasmLocalLayoutPlanner.GetEvaluationStackLocal(
                    stackLocals,
                    index,
                    parameters[index],
                    layouts.Target)))));
            stack.Add(parameters[index]);
        }

        invocations.Emit(
            new JSImportInvocation(import, context, stack, 0, parameters.Length, target, 0),
            code,
            functionIndices);
        if (import.Signature.ReturnType != CliValueKind.Void)
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned(checked((uint)WasmLocalLayoutPlanner.GetEvaluationStackLocal(
                    stackLocals,
                    0,
                    import.Signature.ReturnType,
                    layouts.Target)))));
        }
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        return output.Snapshots.Read();
    }
}
