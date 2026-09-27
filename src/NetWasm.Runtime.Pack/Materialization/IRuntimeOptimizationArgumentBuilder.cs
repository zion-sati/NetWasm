using System.Collections.Immutable;

namespace NetWasm.Runtime.Pack.Materialization;

internal interface IRuntimeOptimizationArgumentBuilder
{
    ImmutableArray<string> Build(RuntimeOptimizationRequest request);
}
