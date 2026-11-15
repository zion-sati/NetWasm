// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Frozen;
using Mono.Cecil;

namespace NetWasm.Sdk.Pack.References;

public sealed record FrameworkContractScope(
    AssemblyNameReference Source,
    AssemblyNameReference Platform,
    FrozenSet<string> ForwardedNames,
    FrozenSet<string> PlatformTypeNames);

public interface IFrameworkContractTypeComparer
{
    bool Compare(TypeReference? source, TypeReference? target, FrameworkContractScope scope);
}
