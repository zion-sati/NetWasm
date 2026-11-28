// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

// Array.InternalCreateSharedInstance: allocates the shared array (SharedArrayShapes) of the rank and boundedness with the
// element's type id in its header, as an array of that element would have it. The bounds address holds the lengths,
// then for a bounded shape the lower bounds. A shape the program allocates no shared array of leaves the result null.
internal sealed class ArrayCreateSharedInstanceIntrinsicEmitter(
    ITypeLayoutProvider types,
    IRuntimeImportResolver imports,
    IRootPublicationEmitter roots,
    IAddressInstructionEmitter addresses,
    IImplicitExceptionEmitter exceptions) : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        var elementId = request.Instruction.Context.NumericTemporaryI4;
        var rank = request.Instruction.Context.NumericTemporaryI4Second;
        var result = request.Local(0, CliValueKind.ManagedReference);
        Get(code, request.Local(0, CliValueKind.I4));
        Set(code, elementId);
        Get(code, request.Local(1, CliValueKind.I4));
        Set(code, rank);
        roots.Emit(request.Instruction, code);
        addresses.Emit(code, 0);
        Set(code, result);
        foreach (var shared in request.Instruction.Target.SharedArrays?.Types ?? [])
        {
            var typeId = types.GetObjectLayout(shared).TypeId;
            var vector = shared.Shape == CliTypeShape.SzArray;
            Get(code, rank);
            I32(code, vector ? 1 : shared.ArrayRank);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
            // A vector is the unbounded rank-one shape and T[*] the bounded one; other ranks take either.
            if (vector || shared.ArrayRank == 1)
            {
                Get(code, request.Local(3, CliValueKind.I4));
                if (vector) code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32And));
            }
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            if (vector)
            {
                Get(code, request.Local(2, CliValueKind.NativeInt));
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load, WasmInstructionOperand.Memory(2, 0)));
                I32(code, typeId);
                Get(code, elementId);
                Call(code, request, RuntimeImportSymbol.AllocateReferenceArray);
                Set(code, result);
            }
            else
            {
                Get(code, request.Local(3, CliValueKind.I4));
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
                Allocate(request, code, typeId, elementId, rank, result, RuntimeImportSymbol.AllocateBoundedRectangularArray);
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
                Allocate(request, code, typeId, elementId, rank, result, RuntimeImportSymbol.AllocateRectangularArray);
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
            }
            Get(code, result);
            addresses.Emit(code, AddressOperation.EqualZero);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            exceptions.Emit(code, ManagedExceptionKind.OutOfMemory, request.Instruction.Context.ManagedThrowFunction);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
    }

    // A rectangular array of references: no element size, and its elements are references.
    private void Allocate(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        int typeId,
        int elementId,
        int rank,
        int result,
        RuntimeImportSymbol import)
    {
        Get(code, rank);
        Get(code, request.Local(2, CliValueKind.NativeInt));
        I32(code, typeId);
        Get(code, elementId);
        I32(code, 0);
        I32(code, 1);
        Call(code, request, import);
        Set(code, result);
    }

    private void Call(IWasmInstructionWriter code, RuntimeIntrinsicEmissionRequest request, RuntimeImportSymbol import) =>
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)imports.Resolve(import, request.Instruction.Target.RuntimeImportSelection))));

    private static void Get(IWasmInstructionWriter code, int local) => code.Write(
        WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)local)));
    private static void Set(IWasmInstructionWriter code, int local) => code.Write(
        WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)local)));
    private static void I32(IWasmInstructionWriter code, int value) => code.Write(
        WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(value)));
}
