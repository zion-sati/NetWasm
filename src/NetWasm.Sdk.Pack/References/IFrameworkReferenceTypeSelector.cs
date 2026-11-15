// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Mono.Cecil;

namespace NetWasm.Sdk.Pack.References;

public sealed record FrameworkReferenceSelection(
    string PackageId,
    AssemblyDefinition Implementation,
    AssemblyDefinition Platform);

/// <summary>Selects framework-owned overlaps; does not certify member compatibility.</summary>
public interface IFrameworkReferenceTypeSelector
{
    IReadOnlyList<string> SelectTypes(FrameworkReferenceSelection request);
}
