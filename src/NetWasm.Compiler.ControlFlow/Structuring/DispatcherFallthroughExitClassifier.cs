// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Structuring;

// A dispatcher exit that leaves the allowed blocks by a fallthrough or branch
// ends the enclosing sequence there, as when a protected code part falls into a
// nested try. Code after the dispatcher cannot join that exit. A leave exits
// through its exception region, and loop breaks and continues keep their own
// transfers, so neither ends the sequence.
internal sealed class DispatcherFallthroughExitClassifier : IDispatcherFallthroughExitClassifier
{
    public bool Classify(
        ControlFlowStructuringState state,
        ImmutableHashSet<int> dispatcher,
        ImmutableHashSet<int> allowed)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(allowed);
        int? loopExit = state.LoopExitTargets.TryPeek(out var exit) ? exit : null;
        int? loopContinue = state.LoopContinueTargets.TryPeek(out var target) ? target : null;
        return dispatcher
            .Where(node => state.Graph.GetBlock(node).Terminator.Operation != CilOperation.Leave)
            .SelectMany(node => state.Graph.Successors[node])
            .Any(successor => !dispatcher.Contains(successor) &&
                !allowed.Contains(successor) &&
                successor != loopExit &&
                successor != loopContinue);
    }
}
