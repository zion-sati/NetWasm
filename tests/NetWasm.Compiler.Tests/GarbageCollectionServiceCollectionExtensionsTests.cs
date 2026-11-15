// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.GarbageCollection;

namespace NetWasm.Compiler.Tests;

public sealed class GarbageCollectionServiceCollectionExtensionsTests
{
    [Fact]
    public void RegistrationExposesRootMapCapabilitiesThroughTheirInterfaces()
    {
        using var provider = new ServiceCollection()
            .AddCompilerGarbageCollection()
            .BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IRootSlotAllocator>());
        Assert.NotNull(provider.GetRequiredService<IRootMapAnalyzerFactory>());
    }
}
