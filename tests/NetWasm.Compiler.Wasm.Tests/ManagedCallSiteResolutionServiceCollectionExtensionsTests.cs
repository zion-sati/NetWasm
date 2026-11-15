// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using NetWasm.Compiler.Wasm.Emission.Instructions.Calls.ManagedCallSites;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class ManagedCallSiteResolutionServiceCollectionExtensionsTests
{
    [Fact]
    public void AddManagedCallSiteResolutionRegistersTheResolverContract()
    {
        var services = new ServiceCollection();

        IServiceCollection returned = services.AddManagedCallSiteResolution();
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Same(services, returned);
        Assert.IsType<ManagedMethodIdentityFactory>(
            provider.GetRequiredService<IManagedMethodIdentityFactory>());
        Assert.IsType<ManagedCallSiteResolver>(provider.GetRequiredService<IManagedCallSiteResolver>());
    }
}
