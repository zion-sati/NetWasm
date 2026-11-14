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
}
