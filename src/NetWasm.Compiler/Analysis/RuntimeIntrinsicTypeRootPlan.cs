// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal sealed record RuntimeIntrinsicTypeRootPlan(
    ImmutableArray<CliTypeIdentity> RuntimeTypes,
    ImmutableArray<CliTypeIdentity> AllocatedTypes)
{
    public static RuntimeIntrinsicTypeRootPlan Empty { get; } = new([], []);

    // Types whose class constructor must be compiled because RunClassConstructor can
    // receive their handle.
    public ImmutableArray<CliTypeIdentity> InitializedTypes { get; init; } = [];

    // Types RuntimeHelpers.GetUninitializedObject may allocate; the analyzer allocates them.
    public ImmutableArray<CliTypeIdentity> UninitializedObjectTypes { get; init; } = [];

    // Types Marshal's structure members can receive, and whether OffsetOf needs field names.
    public ImmutableArray<CliTypeIdentity> StructMarshallingTypes { get; init; } = [];
    public bool StructMarshallingFieldNames { get; init; }

    // Whether PtrToStructure creates structures by their runtime type, which runs the parameterless constructor of
    // each StructMarshallingTypes type that declares one.
    public bool StructMarshallingConstructors { get; init; }
}
