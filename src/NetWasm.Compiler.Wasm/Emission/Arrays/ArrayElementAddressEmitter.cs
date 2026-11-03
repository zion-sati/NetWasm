using System;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Arrays;

internal sealed class ArrayElementAddressEmitter(
    ITargetLayout layouts,
    IAddressInstructionEmitter addresses) : IArrayElementAddressEmitter
{
    public void Emit(InstructionEmissionRequest request, IWasmInstructionWriter code,
        int arrayLocal, int indexLocal, int elementSize)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(code);
        var byteOffset = 0;
        if (request.Header.ArrayBoundsProof is { } proof &&
            proof.Instructions == request.Header.Instructions && proof.Locals == request.Header.Locals &&
            proof.LocalSignatureTypes == request.Header.LocalSignatureTypes &&
            proof.AccessOffsets.Contains(request.Instruction.Offset) &&
            proof.Contains(request.Instruction) &&
            proof.IndexOffsets.TryGetValue(request.Instruction.Offset, out var expression) &&
            !request.Context.ValueLayout.SpilledScalarLocals.Contains(expression.BaseLocal) &&
            request.Header.Locals[expression.BaseLocal] == CliValueKind.I4 &&
            (long)expression.ElementOffset * elementSize is >= 0 and <= int.MaxValue)
        {
            indexLocal = checked(request.Context.LocalBase + expression.BaseLocal);
            byteOffset = expression.ElementOffset * elementSize;
        }
        if (!request.Context.ArrayMetadata.Plan.Accesses.TryGetValue(request.Instruction.Offset, out var slot))
        {
            ManagedMemoryEmitter.EmitArrayElementAddress(code, layouts.Target, arrayLocal, indexLocal,
                request.Context.InlineArrayData, elementSize);
        }
        else
        {
            var snapshot = request.Context.ArrayMetadata.Slots[slot];
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)snapshot.Data)));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)indexLocal)));
            if (layouts.Target.UsesMemory64)
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ExtendI32Unsigned));
            addresses.Emit(code, elementSize);
            code.Write(WasmInstruction.NoOperand(layouts.Target.UsesMemory64
                ? WasmOpcodes.I64Multiply : WasmOpcodes.I32Multiply));
            addresses.Emit(code, AddressOperation.Add);
        }
        if (byteOffset == 0) return;
        addresses.Emit(code, byteOffset);
        addresses.Emit(code, AddressOperation.Add);
    }
}
