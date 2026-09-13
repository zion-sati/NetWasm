using System;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission;

internal interface IInstructionCountingWriterFactory
{
    CountingInstructionWriter Create(IWasmInstructionWriter writer);
}

internal sealed class InstructionCountingWriterFactory :
    IInstructionCountingWriterFactory
{
    public CountingInstructionWriter Create(IWasmInstructionWriter writer) =>
        new(writer);
}

internal sealed class CountingInstructionWriter(IWasmInstructionWriter writer) :
    IWasmInstructionWriter
{
    private readonly IWasmInstructionWriter _writer = writer ??
        throw new ArgumentNullException(nameof(writer));

    public int InstructionCount { get; private set; }

    public void Write(WasmInstruction instruction)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        _writer.Write(instruction);
        InstructionCount++;
    }
}
