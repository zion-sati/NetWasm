using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal interface IImplicitExceptionDiscovery
{
    ImmutableArray<ReachabilityExceptionRequirement> Discover(CilInstruction instruction);
}
