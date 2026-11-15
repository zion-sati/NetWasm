// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.Metadata;

public interface IMetadataCompilationFactory
{
    IMetadataCompilationLease Create(
        ManagedAssembly entry,
        ImmutableDictionary<string, ManagedAssembly> assemblies,
        ImmutableDictionary<string, string> referenceAssemblyAliases,
        ImmutableArray<ManagedAssembly> originalContracts);
}
