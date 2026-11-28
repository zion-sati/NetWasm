// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

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
using NetWasm.Compiler.Wasm.Emission.StructResults;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal interface IJSImportForwarderFunctionEmitter
{
    byte[] Emit(
        MethodInstanceModel import,
        InstructionModuleTarget target,
        IFunctionIndexResolver functionIndices);
}

// Gives a delegate-bound JSImport a managed-ABI body: its parameters become the
// evaluation stack of one JSImport invocation, whose marshalled result is returned. A struct the managed convention
// passes by its fields (a DateTime, a nullable) arrives as those fields and is stored into a scratch frame, whose
// address the stack holds as a struct's.
internal sealed class JSImportForwarderFunctionEmitter(
    ITargetLayout layouts,
    IJSImportInvocationEmitter invocations,
    IGeneratedFunctionWriterFactory writers,
    IStructResultClassifier classifier,
    IValueLayoutProvider values,
    IRuntimeImportResolver runtimeImports) : IJSImportForwarderFunctionEmitter
{
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
        // Each declared parameter's first Wasm parameter, a struct passed by its fields taking one per field, and its
        // place in the scratch frame.
        var layout = StructParameters.Layout(classifier, import.Signature.ParameterSignatureTypes, 0);
        var wasmParameters = layout.IsEmpty ? 0 : layout[^1].First + (layout[^1].Shape?.Results.Length ?? 1);
        var scratchOffsets = new int[parameters.Length];
        var scratchSize = 0;
        for (var index = 0; index < parameters.Length; index++)
        {
            if (layout[index].Shape is null) continue;
            var value = values.GetValueLayout(import.Signature.ParameterSignatureTypes[index]);
            scratchSize = (scratchSize + value.Alignment - 1) & -value.Alignment;
            scratchOffsets[index] = scratchSize;
            scratchSize += value.Size;
        }
        var stackLocals = WasmLocalLayoutPlanner.CreateEvaluationStack(wasmParameters, slotCount);
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
            .. WasmLocalLayoutPlanner.EvaluationStackLocalTypes(slotCount),
            CliValueKind.ManagedReference,
            CliValueKind.ManagedAddress,
            CliValueKind.ManagedReference,
            CliValueKind.ManagedAddress,
            .. WasmLocalLayoutPlanner.TemporaryLocals,
        ];
        var context = new MethodEmissionContext(
            new MethodRootMap(import.Definition.Key, [], []),
            wasmParameters,
            wasmParameters,
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
        if (scratchSize != 0)
        {
            EmitAddress(code, scratchSize);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned(checked((uint)
                runtimeImports.Resolve(RuntimeImportSymbol.ValueFrameEnter, target.RuntimeImportSelection)))));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned(checked((uint)valueFrame))));
        }
        var stack = new List<CliValueKind>(parameters.Length);
        for (var index = 0; index < parameters.Length; index++)
        {
            if (layout[index].Shape is { } shape)
            {
                for (var lane = 0; lane < shape.Fields.Length; lane++)
                {
                    var field = shape.Fields[lane];
                    EmitScratchAddress(code, valueFrame, scratchOffsets[index]);
                    code.Write(WasmInstruction.WithOperand(
                        WasmOpcodes.LocalGet,
                        WasmInstructionOperand.Unsigned(checked((uint)(layout[index].First + lane)))));
                    ManagedMemoryEmitter.EmitStoreByType(code, layouts.Target, field.Offset, field.Type, field.Size);
                }
                EmitScratchAddress(code, valueFrame, scratchOffsets[index]);
            }
            else
            {
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.LocalGet,
                    WasmInstructionOperand.Unsigned(checked((uint)layout[index].First))));
            }
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
        if (scratchSize != 0)
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned(checked((uint)valueFrame))));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned(checked((uint)
                runtimeImports.Resolve(RuntimeImportSymbol.ValueFrameLeave, target.RuntimeImportSelection)))));
        }
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

    private void EmitScratchAddress(IWasmInstructionWriter code, int frame, int offset)
    {
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned(checked((uint)frame))));
        if (offset == 0) return;
        EmitAddress(code, offset);
        code.Write(WasmInstruction.NoOperand(layouts.Target.UsesMemory64 ? WasmOpcodes.I64Add : WasmOpcodes.I32Add));
    }

    private void EmitAddress(IWasmInstructionWriter code, int value) =>
        code.Write(layouts.Target.UsesMemory64
            ? WasmInstruction.WithOperand(WasmOpcodes.I64Constant, WasmInstructionOperand.Signed64(value))
            : WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(value)));
}
