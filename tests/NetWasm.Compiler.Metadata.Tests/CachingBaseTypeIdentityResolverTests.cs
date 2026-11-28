// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class CachingBaseTypeIdentityResolverTests
{
    private static readonly CliTypeIdentity Derived = CliTypeIdentity.Named(new("Test"), "Test", "Derived", false);
    private static readonly CliTypeIdentity Base = CliTypeIdentity.Named(new("Test"), "Test", "Base", false);
    private static readonly CliTypeIdentity Root = CliTypeIdentity.Named(new("Test"), "System", "Object", false);

    [Fact]
    public void EachTypesBaseIsResolvedOnceIncludingATypeWithNone()
    {
        var inner = new CountingResolver();
        var resolver = new CachingBaseTypeIdentityResolver(inner);

        for (var pass = 0; pass < 3; pass++)
        {
            Assert.Equal(Base, resolver.GetBaseTypeIdentity(Derived));
            Assert.Equal(Root, resolver.GetBaseTypeIdentity(Base));
            Assert.Null(resolver.GetBaseTypeIdentity(Root));
        }

        Assert.Equal(3, inner.Calls);
    }

    private sealed class CountingResolver : IBaseTypeIdentityResolver
    {
        private readonly Dictionary<CliTypeIdentity, CliTypeIdentity?> _bases = new()
        {
            [Derived] = Base,
            [Base] = Root,
            [Root] = null,
        };

        public int Calls { get; private set; }

        public CliTypeIdentity? GetBaseTypeIdentity(CliTypeIdentity type)
        {
            Calls++;
            return _bases[type];
        }
    }
}
