// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

public sealed class MetadataAssemblyIdentityTests
{
    [Fact]
    public void EntryAssemblyIdentityUsesCanonicalAssemblyQualifiedDisplay()
    {
        using var context = CompilerInvariantTestContext.Create();

        var metadata = context.Metadata.Snapshot;
        var identity = new AssemblyIdentityFormatter(metadata.Assemblies).Format(
            metadata.EntryAssemblyIdentity);

        Assert.StartsWith(metadata.EntryAssemblyIdentity.Name + ", Version=", identity);
        Assert.Contains(", Culture=", identity, StringComparison.Ordinal);
        Assert.Contains(", PublicKeyToken=", identity, StringComparison.Ordinal);
    }
}
