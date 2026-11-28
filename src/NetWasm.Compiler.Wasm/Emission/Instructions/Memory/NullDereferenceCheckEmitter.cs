// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Memory;

internal interface INullDereferenceCheckEmitter
{
    // Throws NullReferenceException when the object reference or managed address in the local, which the instruction
    // reads or writes through, is null, unless analysis proved every one the instruction dereferences non-null.
    void Emit(InstructionEmissionRequest request, IWasmInstructionWriter code, int local, CliValueKind kind);
}

// Through a null object reference .NET throws NullReferenceException, and through a managed address NetWasm throws it
// where the address is 0, as Mono AOT does (a field access tests its base, an indirect access the address it reads);
// Wasm memory has an address 0, so the check is explicit. A value on the stack is not an address, and an unmanaged
// pointer is not checked.
internal sealed class NullDereferenceCheckEmitter(
    IImplicitExceptionEmitter exceptions,
    IAddressInstructionEmitter addresses) : INullDereferenceCheckEmitter
{
    public void Emit(InstructionEmissionRequest request, IWasmInstructionWriter code, int local, CliValueKind kind)
    {
        if (kind is not (CliValueKind.ManagedReference or CliValueKind.ManagedAddress) || ReceiverNullProof.Proves(request) ||
            kind == CliValueKind.ManagedAddress && FollowsUnalignedPrefix(request))
        {
            return;
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)local)));
        addresses.Emit(code, AddressOperation.EqualZero);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.NullReference, request.Context.ManagedThrowFunction);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    // CilAddressAccesses.IsUnaligned: an unaligned access is not checked, at every level. The prefix stays next to its
    // access through inlining.
    private static bool FollowsUnalignedPrefix(InstructionEmissionRequest request)
    {
        var instructions = request.Header.Instructions;
        var offset = request.Instruction.Offset;
        int low = 0, high = instructions.Length - 1;
        while (low <= high)
        {
            var middle = (low + high) >>> 1;
            var candidate = instructions[middle].Offset;
            if (candidate == offset) return CilAddressAccesses.IsUnaligned(instructions, middle);
            if (candidate < offset) low = middle + 1;
            else high = middle - 1;
        }
        return false;
    }
}
