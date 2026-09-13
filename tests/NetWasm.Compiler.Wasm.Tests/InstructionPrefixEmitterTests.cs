using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class InstructionPrefixEmitterTests
{
    [Fact]
    public void ValidatedPrefixesEmitNoStandaloneBytes()
    {
        var emitter = new InstructionPrefixEmitter();
        foreach (var operation in emitter.Operations)
        {
            var code = new RecordingInstructionWriter();
            var method = new FakeProgram().GetMethod(EntryKey);
            var request = new InstructionEmissionRequest(
                Header(new CilMethodBody(method, 0, [], [])),
                I(0, operation),
                [],
                CreateMethodEmissionContext(0),
                CreateInstructionModuleTarget());
            RegisterInstructionWriter(request, code);

            emitter.Emit(request, CreateFunctionIndexResolver());

            Assert.Empty(GetCodeBytes(request));
        }
    }
}
