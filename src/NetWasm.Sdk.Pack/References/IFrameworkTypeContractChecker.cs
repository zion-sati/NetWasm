// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Mono.Cecil;

namespace NetWasm.Sdk.Pack.References;

public enum FrameworkTypeMismatch
{
    None,
    Declaration,
    RefLike,
    GenericConstraints,
    EnumStorage,
    EnumValues,
    Inheritance
}

public sealed record FrameworkTypeContract(
    TypeDefinition Source,
    TypeDefinition Target,
    IReadOnlyDictionary<string, TypeDefinition> PlatformTypes,
    FrameworkContractScope Scope);

public interface IFrameworkTypeContractChecker
{
    FrameworkTypeMismatch Check(FrameworkTypeContract request);
}
