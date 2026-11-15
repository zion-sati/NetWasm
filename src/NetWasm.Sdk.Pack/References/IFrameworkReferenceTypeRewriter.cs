// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Mono.Cecil;

namespace NetWasm.Sdk.Pack.References;

public sealed record FrameworkReferenceMap(
    ModuleDefinition Source,
    IReadOnlyDictionary<string, TypeReference> ForwardedTypes,
    IReadOnlyDictionary<GenericParameter, TypeReference>? GenericParameters = null);

/// <summary>Substitutes mapped identities in declaration signatures.</summary>
public interface IFrameworkReferenceTypeRewriter
{
    TypeReference Rewrite(TypeReference type, FrameworkReferenceMap map);
    MethodReference Rewrite(MethodReference method, FrameworkReferenceMap map);
}
