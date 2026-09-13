using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Exceptions;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission;

internal sealed class ExceptionObjectStateReader : IExceptionObjectStateReader
{
    private readonly ITargetLayout _target;
    private readonly IRuntimeObjectLayout _runtimeObjects;
    private readonly int? _messageOffset;

    public ExceptionObjectStateReader(
        ITargetLayout target,
        IRuntimeObjectLayout runtimeObjects,
        IExceptionFieldLayoutResolver fields)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _runtimeObjects = runtimeObjects ?? throw new ArgumentNullException(nameof(runtimeObjects));
        ArgumentNullException.ThrowIfNull(fields);
        _messageOffset = fields.Resolve("_message");
    }

    public void Emit(
        IWasmInstructionWriter code,
        int exceptionLocal,
        int typeIdLocal,
        int messageLocal,
        int messageLengthLocal)
    {
        ArgumentNullException.ThrowIfNull(code);

        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)exceptionLocal)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Load,
            WasmInstructionOperand.Memory(2, 0)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)typeIdLocal)));

        if (_messageOffset is null)
        {
            EmitAddressConstant(code, 0);
            WriteLocalSet(code, messageLocal);
            WriteI32Constant(code, 0);
            WriteLocalSet(code, messageLengthLocal);
            return;
        }

        WriteLocalGet(code, exceptionLocal);
        ManagedMemoryEmitter.EmitLoadBySize(
            code,
            _target.Target,
            _messageOffset.Value,
            _target.Target.ObjectReferenceSize);
        WriteLocalSet(code, messageLocal);
        WriteLocalGet(code, messageLocal);
        EmitReferenceEqualZero(code);
        WriteBlock(code, WasmOpcodes.If);
        WriteI32Constant(code, 0);
        WriteLocalSet(code, messageLengthLocal);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        WriteLocalGet(code, messageLocal);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Load,
            WasmInstructionOperand.Memory(2, (uint)_runtimeObjects.StringLengthOffset)));
        WriteLocalSet(code, messageLengthLocal);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitAddressConstant(IWasmInstructionWriter code, int value)
    {
        if (_target.Target.UsesMemory64)
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I64Constant,
                WasmInstructionOperand.Signed64(value)));
        }
        else
        {
            WriteI32Constant(code, value);
        }
    }

    private void EmitReferenceEqualZero(IWasmInstructionWriter code)
    {
        code.Write(WasmInstruction.NoOperand(
            _target.Target.UsesMemory64
                ? WasmOpcodes.I64EqualZero
                : WasmOpcodes.I32EqualZero));
    }

    private static void WriteLocalGet(IWasmInstructionWriter code, int index) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)index)));

    private static void WriteLocalSet(IWasmInstructionWriter code, int index) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)index)));

    private static void WriteI32Constant(IWasmInstructionWriter code, int value) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(value)));

    private static void WriteBlock(IWasmInstructionWriter code, byte opcode) =>
        code.Write(WasmInstruction.WithOperand(
            opcode,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
}
