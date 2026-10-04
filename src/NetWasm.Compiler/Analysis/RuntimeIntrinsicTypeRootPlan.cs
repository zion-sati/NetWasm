using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal sealed record RuntimeIntrinsicTypeRootPlan(
    ImmutableArray<CliTypeIdentity> RuntimeTypes,
    ImmutableArray<CliTypeIdentity> AllocatedTypes)
{
    public static RuntimeIntrinsicTypeRootPlan Empty { get; } = new([], []);
}
