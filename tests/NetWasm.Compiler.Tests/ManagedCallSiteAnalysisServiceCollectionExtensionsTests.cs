// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Analysis.ManagedCallSites;

namespace NetWasm.Compiler.Tests;

public sealed class ManagedCallSiteAnalysisServiceCollectionExtensionsTests
{
    [Fact]
    public void AddManagedCallSiteAnalysisRegistersTheLedgerWriterContract()
    {
        var services = new ServiceCollection();

        IServiceCollection returned = services.AddManagedCallSiteAnalysis();
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Same(services, returned);
        Assert.IsType<ManagedCallSiteLedgerWriter>(
            provider.GetRequiredService<IManagedCallSiteLedgerWriter>());
    }
}
