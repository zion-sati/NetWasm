// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel.Raw;

namespace NetWasm.Compiler.Tests.ComponentModel.Raw;

public sealed class RawModuleLinkingServiceCollectionExtensionsTests
{
    [Fact]
    public void RejectsMissingCollection()
    {
        IServiceCollection? services = null;

        Assert.Throws<ArgumentNullException>(() => services!.AddCompilerRawModuleLinking());
    }

    [Fact]
    public void RegistersOneImplementationForEachRawCapability()
    {
        var services = new ServiceCollection();

        Assert.Same(services, services.AddCompilerRawModuleLinking());

        Assert.Collection(services,
            descriptor => AssertRegistration<IRawModuleLinkInputValidator, RawModuleLinkInputValidator>(descriptor),
            descriptor => AssertRegistration<IRawModuleLinkExecution, RawModuleLinkExecution>(descriptor),
            descriptor => AssertRegistration<IRawModuleLinker, RawModuleLinker>(descriptor));
    }

    [Fact]
    public void PublicCompilerCompositionResolvesRawLinker()
    {
        using var provider = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        Assert.IsType<RawModuleLinker>(provider.GetRequiredService<IRawModuleLinker>());
        Assert.IsType<RawModuleLinkInputValidator>(provider.GetRequiredService<IRawModuleLinkInputValidator>());
        Assert.IsType<RawModuleLinkExecution>(provider.GetRequiredService<IRawModuleLinkExecution>());
    }

    private static void AssertRegistration<TCapability, TImplementation>(ServiceDescriptor descriptor)
    {
        Assert.Equal(typeof(TCapability), descriptor.ServiceType);
        Assert.Equal(typeof(TImplementation), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }
}
