// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions.Startup;

namespace NetWasm.Compiler.Wasm.Tests.Emission.GeneratedFunctions.Startup;

public sealed class StartupGeneratedFunctionServiceCollectionExtensionsTests
{
    [Fact]
    public void RegistersFeatureActorsWithContainerManagedLifetime()
    {
        var services = new ServiceCollection();

        var result = services.AddStartupGeneratedFunctionEmission();

        Assert.Same(services, result);
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IStartupPreflightMethodSelector) &&
            descriptor.ImplementationType == typeof(StartupPreflightMethodSelector) &&
            descriptor.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IStartupPreflightCallEmitter) &&
            descriptor.ImplementationType == typeof(StartupPreflightCallEmitter) &&
            descriptor.Lifetime == ServiceLifetime.Singleton);
    }
}
