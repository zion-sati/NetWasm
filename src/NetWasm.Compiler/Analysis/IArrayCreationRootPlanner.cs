using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal interface IArrayCreationRootPlanner
{
    RuntimeIntrinsicTypeRootPlan Plan(ImmutableArray<CliTypeIdentity> elementSeeds);

    RuntimeIntrinsicTypeRootPlan Plan(
        ImmutableArray<CliTypeIdentity> elementSeeds,
        ImmutableArray<int> ranks);
}
