using System;
using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow;

public sealed class DispatcherBoundaryClipper : IDispatcherBoundaryClipper
{
    public ImmutableHashSet<int> Clip(
        ImmutableHashSet<int> component,
        ImmutableHashSet<int> allowed,
        int? boundary)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(allowed);
        var clipped = component.Intersect(allowed);
        return boundary is int stop ? clipped.Remove(stop) : clipped;
    }
}
