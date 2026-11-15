// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Mono.Cecil;

namespace NetWasm.Sdk.Pack.References;

public sealed record FrameworkTypeHierarchyRequest(
    TypeDefinition Root,
    IReadOnlyDictionary<string, TypeDefinition> PlatformTypes,
    FrameworkContractScope Scope);

public sealed record FrameworkTypeHierarchy(
    IReadOnlyList<TypeReference> BaseTypes,
    IReadOnlyList<TypeReference> Interfaces);

/// <summary>Reads admitted inheritance edges with their generic substitutions.</summary>
public interface IFrameworkTypeHierarchyReader
{
    FrameworkTypeHierarchy Read(FrameworkTypeHierarchyRequest request);
}
