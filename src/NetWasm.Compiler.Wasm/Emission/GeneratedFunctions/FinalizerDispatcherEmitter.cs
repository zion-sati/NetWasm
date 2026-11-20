// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using System;
using System.Linq;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Instructions.Dispatch;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

// The object's type id picks its finalizer, searched among the finalizable types' ids.
internal sealed class FinalizerDispatcherEmitter(
    ITargetLayout layouts,
    IExceptionPayloadBlockEmitter exceptions,
    IGeneratedFunctionWriterFactory writers,
    IKeyedDispatchEmitter keyedDispatch) : IFinalizerDispatcherEmitter
{
    public byte[] Emit(
        FinalizerDispatchPlan[] finalizableTypes,
        IFunctionIndexResolver functionIndices)
    {
        if (finalizableTypes.Length == 0)
        {
            var empty = writers.Create();
            empty.Bytes.Write([0]);
            empty.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Constant,
                WasmInstructionOperand.Signed(2)));
            empty.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
            return empty.Snapshots.Read();
        }

        const int objectParameter = 0;
        const int typeIdParameter = 1;
        const int statusLocal = 2;
        const int exceptionLocal = 3;
        var code = writers.Create();
        WasmLocalDeclarationWriter.Write(
            code.Bytes,
            [CliValueKind.I4, CliValueKind.ManagedReference],
            layouts.Target);
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(2)));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)statusLocal)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.Block, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code.Instructions);
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.TryTable,
            WasmInstructionOperand.TryTableCatch(
                WasmOpcodes.EmptyBlockType,
                0,
                0)));
        keyedDispatch.Emit(code.Instructions, KeyedDispatch.Of(
            typeIdParameter,
            wide: false,
            finalizableTypes.Select(descriptor => ((long)descriptor.TypeId, (Action)(() =>
            {
                code.Instructions.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.LocalGet,
                    WasmInstructionOperand.Unsigned((uint)objectParameter)));
                code.Instructions.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.Call,
                    WasmInstructionOperand.Unsigned((uint)functionIndices.Resolve(
                        descriptor.Finalizer))));
                code.Instructions.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.I32Constant,
                    WasmInstructionOperand.Signed(0)));
                code.Instructions.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.LocalSet,
                    WasmInstructionOperand.Unsigned((uint)statusLocal)));
            }))),
            () => { }));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Branch,
            WasmInstructionOperand.Unsigned(1)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)exceptionLocal)));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(1)));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)statusLocal)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)statusLocal)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        return code.Snapshots.Read();
    }
}
