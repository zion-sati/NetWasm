using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class WasmModuleEmitterTests
{
    [Fact]
    public void ConstructorRejectsMissingMethodRepository()
    {
        Assert.Throws<ArgumentNullException>(() => new WasmModuleEmitter(
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
            null!, null!, null!, null!, null!, null!, null!, null!));
    }

    [Fact]
    public void ConstructorRejectsMissingSymbolFormatter()
    {
        Assert.Throws<ArgumentNullException>(() => new WasmModuleEmitter(
            new FakeProgram(), null!, null!, null!, null!, null!, null!, null!, null!,
            null!, null!, null!, null!, null!, null!, null!, null!, null!));
    }

    [Fact]
    public void ConstructorRejectsMissingTargetLayout()
    {
        Assert.Throws<ArgumentNullException>(() => new WasmModuleEmitter(
            new FakeProgram(), new FakeProgram(), null!, null!, null!, null!, null!,
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!));
    }

    [Fact]
    public void EmitsOnlyFromTheSuppliedImmutableModuleTarget()
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider();
        var request = CreateEmissionRequest(program);
        var services = new ServiceCollection();
        EmitterTestSupport.AddWasmModuleEmission(
            services,
            program,
            new FakeIntrinsics(),
            layouts);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
        var target = provider.GetRequiredService<IWasmModuleTargetFactory>()
            .Create(request);

        var result = provider.GetRequiredService<IWasmModuleEmitter>().Emit(target);

        Assert.NotEmpty(result.Module);
        Assert.Equal(target.ModuleData.StaticDataEnd, result.StaticDataEnd);
    }
}
