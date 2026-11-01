using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class ArrayLoopReductionIntrinsicEmitterTests
{
    [Fact]
    public void RejectsAnotherIntrinsicBeforeWriting()
    {
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.FloatingCopySign, [CliValueKind.F8, CliValueKind.F8]);
        var writer = new EmitterTestSupport.RecordingInstructionWriter();
        var emitter = new ArrayLoopReductionIntrinsicEmitter(new RecordingLayoutProvider());

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ((IRuntimeIntrinsicEmitter)emitter).Emit(request, writer));

        Assert.Contains("is not an array loop reduction", exception.Message);
        Assert.Empty(writer.ToInstructions());
    }
}
