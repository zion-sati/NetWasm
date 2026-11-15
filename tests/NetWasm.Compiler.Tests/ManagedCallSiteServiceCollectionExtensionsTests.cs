// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using NetWasm.Compiler.IntermediateRepresentation.Calls;

namespace NetWasm.Compiler.Tests;

public sealed class ManagedCallSiteServiceCollectionExtensionsTests
{
    [Fact]
    public void AddManagedCallSitesRegistersTheFactoryContract()
    {
        var services = new ServiceCollection();

        IServiceCollection returned = services.AddManagedCallSites();
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Same(services, returned);
        Assert.IsType<ManagedMethodIdentityFactory>(
            provider.GetRequiredService<IManagedMethodIdentityFactory>());
        Assert.IsType<ManagedCallSiteFactory>(provider.GetRequiredService<IManagedCallSiteFactory>());
    }
}
