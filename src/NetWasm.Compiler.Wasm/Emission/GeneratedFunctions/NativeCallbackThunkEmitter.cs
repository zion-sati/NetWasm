// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.NativeInterop;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class NativeCallbackThunkEmitter(
    ITargetLayout layouts,
    IRuntimeImportResolver runtimeImports,
    IAddressInstructionEmitter addresses,
    IStaticInitializationEmitter staticInitialization,
    IManagedTerminalExceptionBoundaryEmitter terminalExceptions,
    IGeneratedFunctionWriterFactory writers) : INativeCallbackThunkEmitter
{
    public byte[] Emit(
        NativeCallbackMethodPlan callback,
        ModuleDataPlan moduleData,
        RuntimeImportSelection runtimeImportSelection,
        IFunctionIndexResolver functionIndices)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentNullException.ThrowIfNull(moduleData);
        ArgumentNullException.ThrowIfNull(functionIndices);
        var readinessAddress = moduleData.NativeCallbackReadinessAddress ??
            throw new InvalidOperationException(
                "Native callback emission requires a planned readiness word.");
        var signature = callback.Abi.PhysicalSignature;
        var code = writers.Create();
        var hasResult = signature.ReturnType != CliValueKind.Void;
        var resultLocal = signature.ParameterTypes.Length;
        var exceptionLocal = resultLocal + (hasResult ? 1 : 0);
        var rootFrameLocal = exceptionLocal + 1;
        var typeIdLocal = rootFrameLocal + 1;
        var messageLocal = typeIdLocal + 1;
        var messageLengthLocal = messageLocal + 1;
        var stackTraceLocal = messageLengthLocal + 1;
        var stackTraceLengthLocal = stackTraceLocal + 1;
        // Struct values the C ABI passes as a scalar, or not at all, and a struct result it returns as a scalar or
        // nothing, live in a value frame the call holds; the C ABI's own copies pass by their addresses.
        var scratch = Scratch(callback.Abi, out var scratchSize);
        var frameLocal = stackTraceLengthLocal + 1;
        var vectorAddressLocal = frameLocal + 1;
        var vectorValueLocal = vectorAddressLocal + 1;
        WasmLocalDeclarationWriter.Write(
            code.Bytes,
            [
                .. hasResult
                    ? [signature.ReturnType]
                    : Array.Empty<CliValueKind>(),
                CliValueKind.ManagedReference,
                CliValueKind.ManagedAddress,
                CliValueKind.I4,
                CliValueKind.ManagedReference,
                CliValueKind.I4,
                CliValueKind.ManagedReference,
                CliValueKind.I4,
                CliValueKind.ManagedAddress,
                CliValueKind.ManagedAddress,
                CliValueKind.V128,
            ],
            layouts.Target);
        var vectors = new VectorScratch(vectorAddressLocal, vectorValueLocal);

        addresses.Emit(code.Instructions, readinessAddress);
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Load,
            WasmInstructionOperand.Memory(2, 0)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.Unreachable));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));

        terminalExceptions.Emit(
            code.Instructions,
            exceptionLocal,
            rootFrameLocal,
            typeIdLocal,
            messageLocal,
            messageLengthLocal,
            stackTraceLocal,
            stackTraceLengthLocal,
            signature.ReturnType,
            resultLocal,
            runtimeImports.Resolve(
                RuntimeImportSymbol.ManagedTerminalExceptionReport,
                runtimeImportSelection),
            runtimeImports.Resolve(
                RuntimeImportSymbol.ManagedTerminalExceptionRaise,
                runtimeImportSelection),
            () =>
            {
                staticInitialization.Emit(
                    new(
                        callback.Method.Definition.DeclaringType,
                        callback.Method.DeclaringType,
                        Lookups.Of(moduleData.StaticInitializerGuards),
                        IsMethodInvocation: true),
                    code.Instructions,
                    functionIndices);
                if (scratchSize != 0)
                {
                    addresses.Emit(code.Instructions, scratchSize);
                    code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned(
                        (uint)runtimeImports.Resolve(RuntimeImportSymbol.ValueFrameEnter, runtimeImportSelection))));
                    Local(WasmOpcodes.LocalSet, frameLocal);
                }
                var result = callback.Abi.Result;
                var structResult = result.LogicalType.StackKind is CliValueKind.ValueType or CliValueKind.V128;
                // A struct result's storage: the C caller's buffer, or the frame. A struct the managed method returns
                // through its leading address, a vector as a value stored there after the call.
                if (structResult)
                {
                    if (result.Kind == NativeAbiValueKind.IndirectAggregate)
                        Local(WasmOpcodes.LocalGet, callback.Abi.HiddenResultParameterIndex!.Value);
                    else
                        FrameAddress(scratch[-1]);
                }
                foreach (var parameter in callback.Abi.Parameters)
                {
                    var value = parameter.Value;
                    var vector = value.LogicalType.StackKind == CliValueKind.V128;
                    switch (value.Kind)
                    {
                        case NativeAbiValueKind.IndirectAggregate:
                            Local(WasmOpcodes.LocalGet, parameter.PhysicalIndex!.Value);
                            if (vector)
                                ManagedMemoryEmitter.EmitVectorLoad(code.Instructions, 0, value.Size, vectors);
                            break;
                        case NativeAbiValueKind.ScalarizedAggregate:
                            FrameAddress(scratch[parameter.LogicalIndex]);
                            Local(WasmOpcodes.LocalGet, parameter.PhysicalIndex!.Value);
                            ManagedMemoryEmitter.EmitStoreByType(code.Instructions, layouts.Target, value.ScalarOffset,
                                value.ScalarStorageType!, value.Size);
                            FrameAddress(scratch[parameter.LogicalIndex]);
                            if (vector)
                                ManagedMemoryEmitter.EmitVectorLoad(code.Instructions, 0, value.Size, vectors);
                            break;
                        case NativeAbiValueKind.IgnoredAggregate:
                            FrameAddress(scratch[parameter.LogicalIndex]);
                            break;
                        default:
                            Local(WasmOpcodes.LocalGet, parameter.PhysicalIndex!.Value);
                            break;
                    }
                }
                code.Instructions.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.Call,
                    WasmInstructionOperand.Unsigned((uint)functionIndices.Resolve(
                        callback.Method))));
                if (structResult && result.LogicalType.StackKind == CliValueKind.V128)
                    ManagedMemoryEmitter.EmitVectorStore(code.Instructions, 0, result.Size, vectors);
                if (structResult && result.Kind == NativeAbiValueKind.ScalarizedAggregate)
                {
                    FrameAddress(scratch[-1]);
                    ManagedMemoryEmitter.EmitLoadByType(code.Instructions, layouts.Target, result.ScalarOffset,
                        result.ScalarStorageType!, result.Size);
                }
                if (hasResult)
                {
                    code.Instructions.Write(WasmInstruction.WithOperand(
                        WasmOpcodes.LocalSet,
                        WasmInstructionOperand.Unsigned((uint)resultLocal)));
                }
                if (scratchSize != 0)
                {
                    Local(WasmOpcodes.LocalGet, frameLocal);
                    code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned(
                        (uint)runtimeImports.Resolve(RuntimeImportSymbol.ValueFrameLeave, runtimeImportSelection))));
                }
            });
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.Return));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        return code.Snapshots.Read();

        void Local(byte opcode, int index) =>
            code.Instructions.Write(WasmInstruction.WithOperand(opcode, WasmInstructionOperand.Unsigned((uint)index)));

        void FrameAddress(int offset)
        {
            Local(WasmOpcodes.LocalGet, frameLocal);
            if (offset == 0)
                return;
            addresses.Emit(code.Instructions, offset);
            addresses.Emit(code.Instructions, AddressOperation.Add);
        }
    }

    // Frame offsets, by logical parameter (the result as -1), for the struct values the C ABI passes as a single
    // scalar or not at all, each at its alignment.
    private static Dictionary<int, int> Scratch(NativeAbiSignaturePlan abi, out int size)
    {
        var offsets = new Dictionary<int, int>();
        var end = 0;
        void Place(int key, NativeAbiValuePlan value)
        {
            var alignment = Math.Max(1, value.Alignment);
            end = (end + alignment - 1) / alignment * alignment;
            offsets[key] = end;
            end += Math.Max(1, value.Size);
        }
        if (abi.Result.LogicalType.StackKind is CliValueKind.ValueType or CliValueKind.V128 &&
            abi.Result.Kind != NativeAbiValueKind.IndirectAggregate)
            Place(-1, abi.Result);
        foreach (var parameter in abi.Parameters)
        {
            if (parameter.Value.Kind is NativeAbiValueKind.ScalarizedAggregate or NativeAbiValueKind.IgnoredAggregate)
                Place(parameter.LogicalIndex, parameter.Value);
        }
        size = (end + 15) / 16 * 16;
        return offsets;
    }
}
