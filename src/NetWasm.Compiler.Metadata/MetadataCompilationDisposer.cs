// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.Metadata;

internal sealed class MetadataCompilationDisposer(
    ImmutableArray<ManagedAssembly> assemblies,
    MetadataLifetime lifetime) : IMetadataCompilationDisposer
{
    public void Dispose()
    {
        if (lifetime.IsDisposed)
        {
            return;
        }

        foreach (var assembly in assemblies)
        {
            assembly.Dispose();
        }

        lifetime.IsDisposed = true;
    }
}
