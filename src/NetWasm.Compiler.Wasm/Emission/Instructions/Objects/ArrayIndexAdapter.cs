using System;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Objects;

internal sealed class ArrayIndexAdapter(
    ITargetLayout layouts,
    IAddressInstructionEmitter addresses,
    IRuntimeObjectLayout objects,
    IImplicitExceptionEmitter exceptions) : IArrayIndexAdapter
{
    public int Adapt(
        InstructionEmissionRequest request,
        IWasmInstructionWriter code,
        int arrayLocal,
        int stackSlot)
    {
        var sourceType = request.Stack[stackSlot];
        if (sourceType is not (CliValueKind.I4 or CliValueKind.NativeInt))
        {
            throw new InvalidOperationException(
                "array index must be int32 or native integer");
        }

        var sourceLocal = GetStackLocal(request.Context, stackSlot, sourceType);
        var proof = request.Header.ArrayBoundsProof is { } candidate &&
            candidate.Describes(request.Header.Instructions, request.Header.Locals, request.Header.LocalSignatureTypes) &&
            candidate.Contains(request.Instruction)
            ? candidate
            : null;
        if (sourceType == CliValueKind.I4 && proof is not null && proof.AccessOffsets.Contains(request.Instruction.Offset))
        {
            return sourceLocal;
        }
        if (!request.Context.ArrayMetadata.Plan.CheckedAccesses.Contains(request.Instruction.Offset) &&
            proof?.ProvesNonNull(request.Instruction.Offset) != true)
        {
            EmitNullCheck(code, arrayLocal, request.Context.ManagedThrowFunction);
        }
        Get(code, sourceLocal);
        if (request.Context.ArrayMetadata.Plan.Accesses.TryGetValue(request.Instruction.Offset, out var slot))
        {
            Get(code, request.Context.ArrayMetadata.Slots[slot].Length);
        }
        else
        {
            Get(code, arrayLocal);
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Load,
                WasmInstructionOperand.Memory(
                    2,
                    (uint)objects.ArrayLengthOffset)));
        }

        if (sourceType == CliValueKind.NativeInt && layouts.Target.UsesMemory64)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ExtendI32Unsigned));
            code.Write(WasmInstruction.NoOperand(
                WasmOpcodes.I64GreaterThanOrEqualUnsigned));
            ThrowIf(code, ManagedExceptionKind.IndexOutOfRange, request.Context.ManagedThrowFunction);

            Get(code, sourceLocal);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32WrapI64));
            Set(code, request.Context.NumericTemporaryI4);
            return request.Context.NumericTemporaryI4;
        }

        code.Write(WasmInstruction.NoOperand(
            WasmOpcodes.I32GreaterThanOrEqualUnsigned));
        ThrowIf(code, ManagedExceptionKind.IndexOutOfRange, request.Context.ManagedThrowFunction);
        return sourceLocal;
    }

    private void EmitNullCheck(
        IWasmInstructionWriter code,
        int arrayLocal,
        OptionalFunctionIndex throwFunction)
    {
        Get(code, arrayLocal);
        addresses.Emit(code, AddressOperation.EqualZero);
        ThrowIf(code, ManagedExceptionKind.NullReference, throwFunction);
    }

    private void ThrowIf(
        IWasmInstructionWriter code,
        ManagedExceptionKind kind,
        OptionalFunctionIndex throwFunction)
    {
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, kind, throwFunction);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private int GetStackLocal(
        MethodEmissionContext context,
        int slot,
        CliValueKind type) => WasmLocalLayoutPlanner.GetEvaluationStackLocal(
        context.StackLocals,
        slot,
        type,
        layouts.Target);

    private static void Get(IWasmInstructionWriter code, int local) => code.Write(
        WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)local)));

    private static void Set(IWasmInstructionWriter code, int local) => code.Write(
        WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)local)));
}
