using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal sealed class ExceptionAwareDispatcherShellBuilder(
    IWholeRegionDispatcherBuilder wholeRegionDispatchers, IExceptionAwareDispatcherBlockSelector blocks) : IExceptionAwareDispatcherShellBuilder
{
    private readonly IWholeRegionDispatcherBuilder _wholeRegionDispatchers =
        wholeRegionDispatchers ?? throw new ArgumentNullException(nameof(wholeRegionDispatchers));
    private readonly IExceptionAwareDispatcherBlockSelector _blocks = blocks ??
        throw new ArgumentNullException(nameof(blocks));

    public ExceptionAwareDispatcherShell Build(ControlFlowStructuringState state,
        int? entry,
        ImmutableHashSet<int> component,
        ImmutableHashSet<int> allowed,
        int? exitStop)
    {
        var owned = _blocks.Select(state, component, allowed);
        var shell = _wholeRegionDispatchers.Build(state, entry, owned);

        return new ExceptionAwareDispatcherShell(shell, owned);
    }
}
