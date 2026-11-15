// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal sealed class ExceptionAwareDispatcherShellBuilder(
    IWholeRegionDispatcherBuilder wholeRegionDispatchers,
    IExceptionAwareDispatcherBlockSelector blocks,
    IDispatcherLoopIslandSelector loopIslands) : IExceptionAwareDispatcherShellBuilder
{
    private readonly IWholeRegionDispatcherBuilder _wholeRegionDispatchers =
        wholeRegionDispatchers ?? throw new ArgumentNullException(nameof(wholeRegionDispatchers));
    private readonly IExceptionAwareDispatcherBlockSelector _blocks = blocks ??
        throw new ArgumentNullException(nameof(blocks));
    private readonly IDispatcherLoopIslandSelector _loopIslands = loopIslands ??
        throw new ArgumentNullException(nameof(loopIslands));

    public ExceptionAwareDispatcherShell Build(ControlFlowStructuringState state,
        int? entry,
        ImmutableHashSet<int> component,
        ImmutableHashSet<int> allowed,
        int? exitStop)
    {
        var exceptionFiltered = _blocks.Select(state, component, allowed);
        var islands = _loopIslands.Select(
            state,
            entry,
            component,
            allowed,
            exceptionFiltered);
        var owned = exceptionFiltered.Except(islands);
        var shell = _wholeRegionDispatchers.Build(state, entry, owned);

        return new ExceptionAwareDispatcherShell(shell, owned);
    }
}
