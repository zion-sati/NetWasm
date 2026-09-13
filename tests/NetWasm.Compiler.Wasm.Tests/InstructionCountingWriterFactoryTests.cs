using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class InstructionCountingWriterFactoryTests
{
    [Fact]
    public void CountsInstructionsWrittenThroughTheCreatedWriter()
    {
        var output = new EmitterTestSupport.RecordingInstructionWriter();
        var writer = new InstructionCountingWriterFactory().Create(output);

        writer.Write(WasmInstruction.NoOperand(WasmOpcodes.Unreachable));
        writer.Write(WasmInstruction.NoOperand(WasmOpcodes.End));

        Assert.Equal(2, writer.InstructionCount);
    }

    [Fact]
    public void RejectsAMissingUnderlyingWriter()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new InstructionCountingWriterFactory().Create(null!));
    }

    [Fact]
    public void RejectsAMissingInstruction()
    {
        var writer = new InstructionCountingWriterFactory().Create(
            new EmitterTestSupport.RecordingInstructionWriter());

        Assert.Throws<ArgumentNullException>(() => writer.Write(null!));
    }
}
