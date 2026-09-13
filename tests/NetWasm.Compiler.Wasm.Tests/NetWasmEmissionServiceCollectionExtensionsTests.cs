using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Wasm.Emission;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class NetWasmEmissionServiceCollectionExtensionsTests
{
    [Fact]
    public void RegistersOneReusableModuleEmitterFactory()
    {
        var services = new ServiceCollection();

        var returned = services.AddNetWasmEmission();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

        Assert.Same(services, returned);
        Assert.IsType<WasmModuleEmitterFactory>(
            provider.GetRequiredService<IWasmModuleEmitterFactory>());
        Assert.Same(
            provider.GetRequiredService<IWasmModuleEmitterFactory>(),
            provider.GetRequiredService<IWasmModuleEmitterFactory>());
    }

    [Fact]
    public void RejectsANullServiceCollection()
    {
        IServiceCollection? services = null;

        Assert.Throws<ArgumentNullException>(() => services!.AddNetWasmEmission());
    }
}
