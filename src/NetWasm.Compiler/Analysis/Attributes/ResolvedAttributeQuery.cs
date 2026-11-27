// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Attributes;

internal sealed record ResolvedAttributeQuery(
    AttributeQueryCall Call,
    CliTypeIdentity? Target,
    CliTypeIdentity Filter,
    bool? InheritConstant)
{
    // The assembly whose own attributes ([assembly: ...]) the query reads, in place of a target type.
    public AssemblyIdentity? TargetAssembly { get; init; }

    // The assembly whose module's attributes ([module: ...]) the query reads, in place of a target type.
    public AssemblyIdentity? TargetModule { get; init; }

    // The element type of the array a retrieval returns: the filter, or Attribute for an open generic filter, whose
    // matches are of different constructed types (as .NET's CreateAttributeArrayHelper makes it).
    public CliTypeIdentity? ResultElementType { get; init; }

    public CliTypeIdentity ResultArrayType => CliTypeIdentity.SzArray(ResultElementType ?? Filter);

    // The filter matches more than one attribute type (it is not sealed, or an open generic definition), so the query
    // may select attributes the program never names: one that cannot be constructed leaves the query to its fallback.
    public bool Broad { get; init; }

    // For an assembly target, the one attribute type's name its attributes are found by, where the filter takes only
    // that type; null reads them all.
    public string? AttributeName { get; init; }
}
