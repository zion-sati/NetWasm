using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Members;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Wasm.Emission.Instructions.Objects;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime.MemberExecution;

internal sealed class MemberArgumentCountCheckEmitter(
    IAddressInstructionEmitter addresses,
    IRuntimeObjectLayout objects,
    IImplicitExceptionEmitter exceptions) : IMemberArgumentCountCheckEmitter
{
    private readonly IAddressInstructionEmitter _addresses = addresses ??
        throw new ArgumentNullException(nameof(addresses));
    private readonly IRuntimeObjectLayout _objects = objects ??
        throw new ArgumentNullException(nameof(objects));
    private readonly IImplicitExceptionEmitter _exceptions = exceptions ??
        throw new ArgumentNullException(nameof(exceptions));

    public void Check(IWasmInstructionWriter code, int argumentsLocal, int expectedCount, OptionalFunctionIndex throwFunction)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (expectedCount == 0)
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)argumentsLocal)));
            _addresses.Emit(code, AddressOperation.EqualZero);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.If,
                WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            EmitArgumentCountMismatch(code, argumentsLocal, expectedCount, throwFunction);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
            return;
        }

        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)argumentsLocal)));
        _addresses.Emit(code, AddressOperation.EqualZero);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        EmitInvalidOperationAndUnreachable(code, throwFunction);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        EmitArgumentCountMismatch(code, argumentsLocal, expectedCount, throwFunction);
    }

    private void EmitArgumentCountMismatch(
        IWasmInstructionWriter code,
        int argumentsLocal,
        int expectedCount,
        OptionalFunctionIndex throwFunction)
    {
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)argumentsLocal)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Load,
            WasmInstructionOperand.Memory(2, (uint)_objects.ArrayLengthOffset)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(expectedCount)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        EmitInvalidOperationAndUnreachable(code, throwFunction);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitInvalidOperationAndUnreachable(
        IWasmInstructionWriter code,
        OptionalFunctionIndex throwFunction)
    {
        _exceptions.Emit(code, ManagedExceptionKind.InvalidOperation, throwFunction);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Unreachable));
    }
}
