using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal interface IRuntimeIntrinsicExceptionRequirementProvider
{
    ImmutableArray<ReachabilityExceptionRequirement> Discover(
        MethodDefinitionModel method);
}
