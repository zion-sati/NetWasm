using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions.LocalTime;

namespace NetWasm.Compiler.Wasm.Tests.Emission.GeneratedFunctions.LocalTime;

public sealed class LocalTimeGeneratedFunctionServiceCollectionExtensionsTests
{
    [Fact]
    public void RegistersFeatureActorsWithContainerManagedLifetime()
    {
        var services = new ServiceCollection();

        var result = services.AddLocalTimeGeneratedFunctionEmission();

        Assert.Same(services, result);
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(ILocalTimePreflightMethodSelector) &&
            descriptor.ImplementationType == typeof(LocalTimePreflightMethodSelector) &&
            descriptor.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(ILocalTimePreflightCallEmitter) &&
            descriptor.ImplementationType == typeof(LocalTimePreflightCallEmitter) &&
            descriptor.Lifetime == ServiceLifetime.Singleton);
    }
}
