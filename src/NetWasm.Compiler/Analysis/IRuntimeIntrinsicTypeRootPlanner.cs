using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal interface IRuntimeIntrinsicTypeRootPlanner
{
    ImmutableArray<CliTypeIdentity> Plan(
        IEnumerable<MethodInstanceModel> reachableMethods,
        IEnumerable<CliTypeIdentity> constructedTypes);
}
