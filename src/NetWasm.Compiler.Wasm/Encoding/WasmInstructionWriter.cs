using System;

namespace NetWasm.Compiler.Wasm.Encoding;

/// <summary>
/// Command writer that selects and delegates to one operand-shape Strategy.
/// </summary>
public sealed class WasmInstructionWriter : IWasmInstructionWriter
{
    private readonly IWasmBinaryWriter _writer;
    private readonly IWasmInstructionEncoderRegistry _encoders;

    public WasmInstructionWriter(
        IWasmBinaryWriter writer,
        IWasmInstructionEncoderRegistry encoders)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(encoders);
        _writer = writer;
        _encoders = encoders;
    }

    public WasmInstructionWriter(IWasmBinaryWriter writer)
        : this(writer, WasmInstructionEncoderRegistryFactory.CreateDefault())
    {
    }

    public void Write(WasmInstruction instruction)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        _encoders.Resolve(instruction.OperandShape).Encode(_writer, instruction);
    }
}
