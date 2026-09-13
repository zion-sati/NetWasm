using System;

namespace NetWasm.Compiler.Wasm.Encoding;

/// <summary>
/// Passive immutable Command representing one WebAssembly instruction.
/// </summary>
public sealed record WasmInstruction
{
    public WasmInstruction(byte opcode)
        : this(opcode, WasmInstructionOperand.None)
    {
    }

    public WasmInstruction(byte opcode, WasmInstructionOperand operand)
    {
        ArgumentNullException.ThrowIfNull(operand);
        Opcode = opcode;
        Operand = operand;
    }

    public byte Opcode { get; }

    public WasmInstructionOperand Operand { get; }

    public WasmInstructionOperandShape OperandShape => Operand.Shape;

    public static WasmInstruction NoOperand(byte opcode) => new(opcode);

    public static WasmInstruction WithOperand(byte opcode, WasmInstructionOperand operand) =>
        new(opcode, operand);
}
