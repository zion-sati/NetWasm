using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow;

public interface IDispatcherBoundaryClipper
{
    ImmutableHashSet<int> Clip(
        ImmutableHashSet<int> component,
        ImmutableHashSet<int> allowed,
        int? boundary);
}
