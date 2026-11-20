// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using NetWasm.Runtime.Pack.Materialization;

namespace NetWasm.Runtime.Pack.Tests.Materialization;

public sealed class RuntimeExceptionSystemLibrariesTests
{
    private static readonly ImmutableArray<string> Ordinary = ["/s/libc.a", "/s/libstandalonewasm-nocatch-memgrow.a", "/s/libc++-noexcept.a"];
    private static readonly ImmutableArray<string> Exceptions =
        ["/s/libc.a", "/s/libstandalonewasm-memgrow.a", "/s/libc++-wasmexcept.a", "/s/libunwind-wasmexcept.a"];

    // A program with C++ exceptions takes the whole exception closure; others keep the ordinary one.
    [Fact]
    public void TakesTheExceptionClosureOnlyForCxxExceptions()
    {
        Assert.Equal(Exceptions, RuntimeExceptionSystemLibraries.Select(Ordinary, Exceptions, cxxExceptions: true));
        Assert.Equal(Ordinary, RuntimeExceptionSystemLibraries.Select(Ordinary, Exceptions, cxxExceptions: false));
        Assert.Equal(Ordinary, RuntimeExceptionSystemLibraries.Select(Ordinary, [], cxxExceptions: false));
    }

    [Fact]
    public void RequiresBothListsAndAnExceptionClosureWhenUsed()
    {
        Assert.Throws<ArgumentException>(() => RuntimeExceptionSystemLibraries.Select(default, [], false));
        Assert.Throws<ArgumentException>(() => RuntimeExceptionSystemLibraries.Select([], default, false));
        var error = Assert.Throws<InvalidOperationException>(() => RuntimeExceptionSystemLibraries.Select(Ordinary, [], true));
        Assert.Contains("no C++ exception libraries", error.Message, StringComparison.Ordinal);
    }
}
