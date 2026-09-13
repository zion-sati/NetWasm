using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal interface IStringConstructionExceptionRequirementProvider
{
    ImmutableArray<ReachabilityExceptionRequirement> Discover(
        MethodDefinitionModel constructor);
}
