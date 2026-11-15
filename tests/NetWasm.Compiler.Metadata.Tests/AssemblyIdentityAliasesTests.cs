// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class AssemblyIdentityAliasesTests
{
    [Fact]
    public void CanonicalizesAliasesAndPreservesUnmappedIdentities()
    {
        var aliases = new AssemblyIdentityAliases(
            ImmutableDictionary<string, string>.Empty.Add(
                "Reference",
                "Implementation"));

        Assert.Equal(
            new AssemblyIdentity("Implementation"),
            aliases.Canonicalize(new AssemblyIdentity("Reference")));
        Assert.Equal(
            new AssemblyIdentity("Unmapped"),
            aliases.Canonicalize(new AssemblyIdentity("Unmapped")));
        Assert.Equal(
            new AssemblyIdentity("Reference"),
            AssemblyIdentityAliases.Empty.Canonicalize(
                new AssemblyIdentity("Reference")));
        Assert.Equal(
            new AssemblyIdentity("Reference"),
            default(AssemblyIdentityAliases).Canonicalize(
                new AssemblyIdentity("Reference")));
    }

    [Fact]
    public void RejectsNullAliasMaps()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new AssemblyIdentityAliases(null!));
    }
}
