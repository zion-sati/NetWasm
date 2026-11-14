using NetWasm.Compiler.Core;
using System.Collections.Immutable;

namespace NetWasm.Compiler.Analysis;

internal sealed record ReachableMethodRequest(MethodInstanceModel Method)
{
    public ImmutableStack<CompilerDependencyStep> DependencyPath { get; init; } = ImmutableStack<CompilerDependencyStep>.Empty;

    public CilMethodBody? RewrittenBody { get; init; }
}
