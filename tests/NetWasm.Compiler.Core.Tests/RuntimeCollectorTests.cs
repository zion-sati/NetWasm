// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Core.Tests;

// The runtime's collectors by the names NetWasmGarbageCollector gives them, and which of them has generations, so
// that a program for it records every reference stored into memory.
public sealed class RuntimeCollectorTests
{
    [Fact]
    public void TheCollectorsAreTheRuntimesAndOnlyNetWasmGenHasGenerations()
    {
        Assert.Equal(["Compact", "NetWasm", "NetWasmGen", "Bump"], RuntimeCollector.All.Select(collector => collector.Name));
        Assert.Equal([RuntimeCollector.NetWasmGen], RuntimeCollector.All.Where(collector => collector.HasGenerations));
        Assert.Equal(
            [RuntimeCollector.Compact, RuntimeCollector.NetWasm, RuntimeCollector.NetWasmGen, RuntimeCollector.Bump],
            RuntimeCollector.All);
    }

    [Theory]
    [InlineData("Compact")]
    [InlineData("NetWasm")]
    [InlineData("NetWasmGen")]
    [InlineData("Bump")]
    public void ACollectorIsFoundByExactlyItsName(string name)
    {
        var collector = RuntimeCollector.Named(name);

        Assert.NotNull(collector);
        Assert.Equal(name, collector.Name);
        Assert.Same(collector, RuntimeCollector.All.Single(candidate => candidate.Name == name));
    }

    [Theory]
    [InlineData("netwasmgen")]
    [InlineData("NETWASM")]
    [InlineData(" NetWasm")]
    [InlineData("Boehm")]
    [InlineData("")]
    public void AnyOtherSpellingNamesNoCollector(string name) => Assert.Null(RuntimeCollector.Named(name));

    [Fact]
    public void ANameIsRequired() => Assert.Throws<ArgumentNullException>(() => RuntimeCollector.Named(null!));
}
