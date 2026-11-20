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

    // The element type of the array a retrieval returns: the filter, or Attribute for an open generic filter, whose
    // matches are of different constructed types (as .NET's CreateAttributeArrayHelper makes it).
    public CliTypeIdentity? ResultElementType { get; init; }

    public CliTypeIdentity ResultArrayType => CliTypeIdentity.SzArray(ResultElementType ?? Filter);
}
