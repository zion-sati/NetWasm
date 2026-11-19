// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Dispatch;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

// A delegate's clone: a new object of its exact type with its four fields copied. A delegate type declares no fields of
// its own (ECMA-335 II.14.6), so its instances are one size; the source's type id, in a run of delegate type ids of one
// size, allocates the copy. Ids between them that are no delegate's never reach the clone of a delegate.
internal sealed class DelegateCloneIntrinsicEmitter(
    ITargetLayout layouts,
    ITypeLayoutProvider types,
    IRuntimeObjectLayout objects,
    IRuntimeImportResolver runtimeImports,
    IImplicitExceptionEmitter exceptions,
    IKeyedDispatchEmitter keyedDispatch) : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        var source = request.Local(0, CliValueKind.ManagedReference);
        var delegates = request.Instruction.Target.DelegateTypes.Read()
            .Select(type => types.GetObjectLayout(type))
            .Select(layout => (Id: layout.TypeId, layout.Size))
            .Distinct()
            .OrderBy(entry => entry.Id)
            .ToArray();
        var runs = new List<(KeyRange Range, int Size)>();
        foreach (var (id, size) in delegates)
        {
            if (runs.Count != 0 && runs[^1].Size == size)
            {
                runs[^1] = (runs[^1].Range with { High = id }, size);
            }
            else
            {
                runs.Add((new(id, id), size));
            }
        }
        var typeIdLocal = request.Instruction.Context.NumericTemporaryI4;
        WriteLocalGet(code, source);
        WriteI32Load(code, 0);
        WriteLocalSet(code, typeIdLocal);
        keyedDispatch.Emit(code, new(
            typeIdLocal,
            wide: false,
            [.. runs.Select(run => run.Range)],
            index => EmitClone(request, code, source, typeIdLocal, runs[index].Size),
            () => exceptions.Emit(code, ManagedExceptionKind.InvalidCast, request.Instruction.Context.ManagedThrowFunction)));
    }

    private void EmitClone(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code, int source, int typeIdLocal, int size)
    {
        var clone = request.Instruction.Context.ObjectTemporary;
        WriteAddressConstant(code, size);
        WriteLocalGet(code, typeIdLocal);
        WriteCall(code, runtimeImports.Resolve(RuntimeImportSymbol.Allocate));
        WriteLocalSet(code, clone);
        EmitAllocationFailureCheck(code, clone, request.Instruction.Context.ManagedThrowFunction);
        CopyField(code, source, clone, objects.DelegateTargetOffset, layouts.Target.ObjectReferenceSize);
        CopyField(code, source, clone, objects.DelegateMethodIdOffset, sizeof(int));
        CopyField(code, source, clone, objects.DelegateInvocationListOffset, layouts.Target.ObjectReferenceSize);
        CopyField(code, source, clone, objects.DelegateInvocationCountOffset, sizeof(int));
        WriteLocalGet(code, clone);
        WriteLocalSet(code, source);
    }

    private void CopyField(
        IWasmInstructionWriter code,
        int source,
        int destination,
        int offset,
        int size)
    {
        WriteLocalGet(code, destination);
        WriteLocalGet(code, source);
        ManagedMemoryEmitter.EmitLoadBySize(code, layouts.Target, offset, size);
        ManagedMemoryEmitter.EmitStoreBySize(code, layouts.Target, offset, size);
    }

    private void EmitAllocationFailureCheck(
        IWasmInstructionWriter code,
        int objectLocal,
        OptionalFunctionIndex throwFunction)
    {
        WriteLocalGet(code, objectLocal);
        code.Write(WasmInstruction.NoOperand(
            layouts.Target.UsesMemory64
                ? WasmOpcodes.I64EqualZero
                : WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.OutOfMemory, throwFunction);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void WriteAddressConstant(IWasmInstructionWriter code, int value) =>
        code.Write(WasmInstruction.WithOperand(
            layouts.Target.UsesMemory64
                ? WasmOpcodes.I64Constant
                : WasmOpcodes.I32Constant,
            layouts.Target.UsesMemory64
                ? WasmInstructionOperand.Signed64(value)
                : WasmInstructionOperand.Signed(value)));

    private static void WriteI32Load(IWasmInstructionWriter code, int offset) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Load,
            WasmInstructionOperand.Memory(2, checked((uint)offset))));

    private static void WriteLocalGet(IWasmInstructionWriter code, int index) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(checked((uint)index))));

    private static void WriteLocalSet(IWasmInstructionWriter code, int index) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned(checked((uint)index))));

    private static void WriteCall(IWasmInstructionWriter code, int index) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned(checked((uint)index))));
}
