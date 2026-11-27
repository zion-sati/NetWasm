// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Wasm.Emission.Instructions.Memory;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Encoding;
using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.AddressTakenValues;

namespace NetWasm.Compiler.Wasm.Emission.Instructions;

internal sealed class ValueObjectBlockMemoryEmitter(
    ITargetLayout layouts,
    IValueLayoutProvider values,
    ICilTypeOperandResolver types,
    IRuntimeImportResolver runtimeImports,
    IAddressTakenAggregateProjectionEmitter aggregateProjections,
    IReferenceWriteBarrierEmitter writeBarrier,
    INullDereferenceCheckEmitter nullChecks) : InstructionCommandProvider
{
    public override ImmutableArray<InstructionCommand> Commands =>
    [
        Command(CilOperation.InitializeObject, EmitInitializeObject),
        Command(CilOperation.SizeOf, EmitSizeOf),
        Command(CilOperation.LocalAllocate, EmitLocalAllocate),
        Command(CilOperation.CopyBlock, EmitCopyBlock),
        Command(CilOperation.InitializeBlock, EmitInitializeBlock),
        Command(CilOperation.DefaultValue, EmitDefaultValue),
        Command(CilOperation.LoadObject, EmitLoadObject),
        Command(CilOperation.StoreObject, EmitStoreObject),
        Command(CilOperation.CopyObject, EmitCopyObject),
    ];

    private static InstructionCommand Command(
        CilOperation operation,
        Action<InstructionEmissionRequest, IWasmInstructionWriter> emit) => new(
        operation,
        InstructionFamily.ValueObjectBlockMemory,
        emit);

    private void EmitSizeOf(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        var size = values.GetValueLayout(types.Resolve(request.Instruction, request.Header.MethodInstance)).Size;
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(size)));
        PushLocal(request, code, CliValueKind.I4);
    }

    private void EmitInitializeObject(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        // An argument temporary in lanes clears its lanes.
        if (request.Context.ArgumentLanes.Clears.TryGetValue(request.Instruction.Offset, out var argumentTemporary))
        {
            var shape = request.Context.ArgumentLanes.Locals[argumentTemporary];
            var temporaryLanes = request.Context.ArgumentLaneLocals[argumentTemporary];
            for (var lane = 0; lane < temporaryLanes.Length; lane++)
            {
                StructResults.ZeroValues.Emit(code, shape.Fields[lane].Kind, layouts.Target);
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)temporaryLanes[lane])));
            }
            request.Stack.RemoveAt(request.Stack.Count - 1);
            return;
        }
        // A returned local in lanes clears its lanes.
        if (request.Context.ReturnLanes is { } lanes && lanes.Clears.Contains(request.Instruction.Offset))
        {
            var laneLocals = request.Context.StructResultLocals.For(lanes.Shape);
            for (var lane = 0; lane < laneLocals.Length; lane++)
            {
                StructResults.ZeroValues.Emit(code, lanes.Shape.Fields[lane].Kind, layouts.Target);
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)laneLocals[lane])));
            }
            request.Stack.RemoveAt(request.Stack.Count - 1);
            return;
        }
        var slot = request.Stack.Count - 1;
        var layout = values.GetValueLayout(types.Resolve(request.Instruction, request.Header.MethodInstance));
        var address = GetStackLocal(request.Context, slot, request.Stack[slot]);
        nullChecks.Emit(request, code, address, request.Stack[slot]);
        if (!SmallBlockEmitter.TryEmitKnownSizeZero(code, address, layout.Size))
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)address)));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(0)));
            EmitAddressConstant(code, layout.Size);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.Prefixed, WasmInstructionOperand.PrefixedPair(WasmOpcodes.MemoryFill, 0)));
        }
        request.Stack.RemoveAt(slot);
    }

    private void EmitLocalAllocate(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        var slot = request.Stack.Count - 1;
        var sizeType = request.Stack[slot];
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(request.Context, slot, sizeType)))));
        if (layouts.Target.UsesMemory64 && sizeType == CliValueKind.I4)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ExtendI32Unsigned));
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)(runtimeImports.Resolve(RuntimeImportSymbol.ValueFrameEnter)))));
        if (request.Header.InitializesLocals)
        {
            EmitZeroLocalAllocation(request, code, GetStackLocal(request.Context, slot, sizeType), sizeType);
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
            request.Context,
            slot,
            CliValueKind.NativeInt)))));
        request.Stack[slot] = CliValueKind.NativeInt;
    }

    // Under localsinit, localloc's block is zeroed (ECMA-335 III.3.47). The block's address,
    // on the stack, goes through the object temporary, since the size's local and the result's
    // are one Wasm local on wasm32; a failed allocation (a null address) is not filled.
    private void EmitZeroLocalAllocation(
        InstructionEmissionRequest request,
        IWasmInstructionWriter code,
        int sizeLocal,
        CliValueKind sizeType)
    {
        var address = request.Context.ObjectTemporary;
        var memory64 = layouts.Target.UsesMemory64;
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalTee, WasmInstructionOperand.Unsigned((uint)address)));
        code.Write(WasmInstruction.NoOperand(memory64 ? WasmOpcodes.I64EqualZero : WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)address)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(0)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)sizeLocal)));
        if (memory64 && sizeType == CliValueKind.I4)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ExtendI32Unsigned));
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Prefixed, WasmInstructionOperand.PrefixedPair(WasmOpcodes.MemoryFill, 0)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)address)));
    }

    // cpblk and initblk copy and fill through the small-block cascade, so a
    // constant length, as Unsafe.CopyBlock's and InitBlock's usually are, folds
    // to its inline chunks where the target inlines small blocks.
    private void EmitCopyBlock(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        var destination = request.Stack.Count - 3;
        SmallBlockEmitter.EmitCopy(
            code,
            layouts.Target,
            request.Target.InlineSmallBlockOperations,
            GetStackLocal(request.Context, destination, request.Stack[destination]),
            GetStackLocal(request.Context, destination + 1, request.Stack[destination + 1]),
            BlockLength(request, code, destination + 2));
        request.Stack.RemoveRange(destination, 3);
    }

    private void EmitInitializeBlock(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        var destination = request.Stack.Count - 3;
        SmallBlockEmitter.EmitFill(
            code,
            layouts.Target,
            request.Target.InlineSmallBlockOperations,
            GetStackLocal(request.Context, destination, request.Stack[destination]),
            GetStackLocal(request.Context, destination + 1, CliValueKind.I4),
            BlockLength(request, code, destination + 2));
        request.Stack.RemoveRange(destination, 3);
    }

    // The local holding the length at the target's address width: wasm64
    // widens a 32-bit length into the numeric temporary.
    private int BlockLength(InstructionEmissionRequest request, IWasmInstructionWriter code, int slot)
    {
        var type = request.Stack[slot];
        var local = GetStackLocal(request.Context, slot, type);
        if (!layouts.Target.UsesMemory64 || type != CliValueKind.I4)
        {
            return local;
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)local)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ExtendI32Unsigned));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)request.Context.NumericTemporaryI8)));
        return request.Context.NumericTemporaryI8;
    }

    private void EmitDefaultValue(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        var type = types.Resolve(request.Instruction, request.Header.MethodInstance);
        if (type.StackKind != CliValueKind.ValueType)
        {
            // The zero of the type's own Wasm type: an i64 for a long or a long-backed enum, and for a native integer
            // or a reference on wasm64; an f32 or f64 for float and double; a v128 for a vector.
            StructResults.ZeroValues.Emit(code, type.StackKind, layouts.Target);
            PushLocal(request, code, type.StackKind);
            return;
        }
        var layout = values.GetValueLayout(type);
        var offset = request.Context.ValueLayout.TemporaryOffsets[
            request.Instruction.Offset];
        EmitValueFrameAddress(code, request.Context, offset);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(request.Context.ObjectTemporary))));
        if (!SmallBlockEmitter.TryEmitKnownSizeZero(code, request.Context.ObjectTemporary, layout.Size))
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(request.Context.ObjectTemporary))));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(0)));
            EmitAddressConstant(code, layout.Size);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.Prefixed, WasmInstructionOperand.PrefixedPair(WasmOpcodes.MemoryFill, 0)));
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(request.Context.ObjectTemporary))));
        PushLocal(request, code, CliValueKind.ValueType);
    }

    private void EmitLoadObject(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        if (aggregateProjections.TryEmit(request, code))
        {
            return;
        }
        var slot = request.Stack.Count - 1;
        nullChecks.Emit(request, code, GetStackLocal(request.Context, slot, request.Stack[slot]), request.Stack[slot]);
        var type = types.Resolve(request.Instruction, request.Header.MethodInstance);
        var layout = values.GetValueLayout(type);
        if (type.StackKind == CliValueKind.ValueType &&
            request.Context.ValueLayout.DirectValueLoadOffsets.Contains(request.Instruction.Offset))
        {
            // The adjacent consumer copies the value from its source address.
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)GetStackLocal(
                request.Context,
                slot,
                request.Stack[slot]))));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)GetStackLocal(
                request.Context,
                slot,
                CliValueKind.ValueType))));
            request.Stack[slot] = CliValueKind.ValueType;
            return;
        }
        if (type.StackKind == CliValueKind.ValueType)
        {
            var offset = request.Context.ValueLayout.TemporaryOffsets[
                request.Instruction.Offset];
            EmitValueFrameAddress(code, request.Context, offset);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalTee, WasmInstructionOperand.Unsigned((uint)(request.Context.ObjectTemporary))));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
                request.Context,
                slot,
                request.Stack[slot])))));
            SmallBlockEmitter.EmitKnownSizeCopy(code, request.Context, layout.Size, SmallBlockEmitter.WidestChunk(layout),
                writer => EmitAddressConstant(writer, layout.Size));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(request.Context.ObjectTemporary))));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
                request.Context,
                slot,
                CliValueKind.ValueType)))));
            request.Stack[slot] = CliValueKind.ValueType;
            return;
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
            request.Context,
            slot,
            request.Stack[slot])))));
        ManagedMemoryEmitter.EmitLoadByType(
            code,
            layouts.Target,
            0,
            type,
            layout.Size, scratch: request.Context.VectorScratch);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(request.Context, slot, type.StackKind)))));
        request.Stack[slot] = type.StackKind;
    }

    private void EmitStoreObject(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        if (aggregateProjections.TryEmit(request, code))
        {
            return;
        }
        var destination = request.Stack.Count - 2;
        var type = types.Resolve(request.Instruction, request.Header.MethodInstance);
        var layout = values.GetValueLayout(type);
        nullChecks.Emit(request, code, GetStackLocal(request.Context, destination, request.Stack[destination]),
            request.Stack[destination]);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
            request.Context,
            destination,
            request.Stack[destination])))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
            request.Context,
            destination + 1,
            type.StackKind)))));
        if (type.StackKind == CliValueKind.ValueType)
        {
            SmallBlockEmitter.EmitKnownSizeCopy(code, request.Context, layout.Size, SmallBlockEmitter.WidestChunk(layout),
                writer => EmitAddressConstant(writer, layout.Size));
        }
        else
        {
            ManagedMemoryEmitter.EmitStoreByType(
                code,
                layouts.Target,
                0,
                type,
                layout.Size, scratch: request.Context.VectorScratch);
        }
        writeBarrier.Emit(request, code,
            GetStackLocal(request.Context, destination, request.Stack[destination]), type);
        request.Stack.RemoveRange(destination, 2);
    }

    private void EmitCopyObject(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        var destination = request.Stack.Count - 2;
        var type = types.Resolve(request.Instruction, request.Header.MethodInstance);
        var layout = values.GetValueLayout(type);
        // The destination, then the source, as desktop .NET dereferences them.
        nullChecks.Emit(request, code, GetStackLocal(request.Context, destination, request.Stack[destination]),
            request.Stack[destination]);
        nullChecks.Emit(request, code, GetStackLocal(request.Context, destination + 1, request.Stack[destination + 1]),
            request.Stack[destination + 1]);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
            request.Context,
            destination,
            request.Stack[destination])))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
            request.Context,
            destination + 1,
            request.Stack[destination + 1])))));
        SmallBlockEmitter.EmitKnownSizeCopy(code, request.Context, layout.Size, SmallBlockEmitter.WidestChunk(layout),
            writer => EmitAddressConstant(writer, layout.Size));
        writeBarrier.Emit(request, code,
            GetStackLocal(request.Context, destination, request.Stack[destination]), type);
        request.Stack.RemoveRange(destination, 2);
    }

    private void PushLocal(InstructionEmissionRequest request, IWasmInstructionWriter code, CliValueKind type)
    {
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
            request.Context,
            request.Stack.Count,
            type)))));
        request.Stack.Add(type);
    }

    private void EmitValueFrameAddress(
        IWasmInstructionWriter code,
        MethodEmissionContext context,
        int offset)
    {
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(context.ValueFrame))));
        if (offset == 0)
        {
            return;
        }
        EmitAddressConstant(code, offset);
        if (layouts.Target.UsesMemory64)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64Add));
        }
        else
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Add));
        }
    }

    private void EmitAddressConstant(IWasmInstructionWriter code, int value)
    {
        if (layouts.Target.UsesMemory64)
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I64Constant, WasmInstructionOperand.Signed64(value)));
        }
        else
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(value)));
        }
    }

    private int GetStackLocal(
        MethodEmissionContext context,
        int slot,
        CliValueKind type) => WasmLocalLayoutPlanner.GetEvaluationStackLocal(
        context.StackLocals,
        slot,
        type,
        layouts.Target);
}
