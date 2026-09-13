using NetWasm.Compiler.Wasm.Emission;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class WasmModuleEmitterFactoryTests
{
    [Fact]
    public void ComposesAndDisposesAFreshEmitterGraphPerModule()
    {
        var program = new FakeProgram();
        var request = CreateEmissionRequest(program);
        var factory = new WasmModuleEmitterFactory();

        var first = EmitModule(
            factory,
            program,
            new FakeIntrinsics(),
            new RecordingLayoutProvider(),
            request);
        var second = EmitModule(
            factory,
            program,
            new FakeIntrinsics(),
            new RecordingLayoutProvider(),
            request);

        Assert.NotEmpty(first.Module);
        Assert.Equal(first.Module, second.Module);
        Assert.Equal(first.StaticDataEnd, second.StaticDataEnd);
    }
}
