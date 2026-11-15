// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using System.Linq;

namespace NetWasm.Compiler.Metadata;

public sealed class MetadataCompilationFactory : IMetadataCompilationFactory
{
    public IMetadataCompilationLease Create(
        ManagedAssembly entry,
        ImmutableDictionary<string, ManagedAssembly> assemblies,
        ImmutableDictionary<string, string> referenceAssemblyAliases,
        ImmutableArray<ManagedAssembly> originalContracts)
    {
        var lifetime = new MetadataLifetime();
        var orderedAssemblies = assemblies.Values
            .OrderBy(assembly => assembly.Identity.Name, StringComparer.Ordinal)
            .ToImmutableArray();

        return new MetadataCompilationLease(
            entry,
            orderedAssemblies,
            referenceAssemblyAliases,
            new MetadataCompilationDisposer(orderedAssemblies.AddRange(originalContracts), lifetime),
            lifetime,
            originalContracts);
    }
}
