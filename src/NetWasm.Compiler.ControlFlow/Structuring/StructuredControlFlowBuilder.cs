using NetWasm.Compiler.ControlFlow.Draft;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal sealed class StructuredControlFlowBuilder(
    IExceptionAwareDispatcherShellBuilder exceptionAwareDispatcherShells,
    IStructuredSequenceStepExecutor sequenceSteps) : IStructuredControlFlowBuilder
{
    private readonly IExceptionAwareDispatcherShellBuilder _exceptionAwareDispatcherShells =
        exceptionAwareDispatcherShells ??
        throw new ArgumentNullException(nameof(exceptionAwareDispatcherShells));
    private readonly IStructuredSequenceStepExecutor _sequenceSteps = sequenceSteps ??
        throw new ArgumentNullException(nameof(sequenceSteps));

    public StructuredSequenceDraft Build(ControlFlowStructuringState state,
        int? start,
        int? stop,
        ImmutableHashSet<int> allowed,
        HashSet<int> path)
    {
        var regions = ImmutableArray.CreateBuilder<StructuredRegionDraft>();
        var current = start;
        while (current is not null && current != stop)
        {
            if (state.ActiveDispatchers.TryPeek(out var activeDispatcher) &&
                            activeDispatcher.Contains(current.Value))
            {
                regions.Add(new StructuredDispatcherContinueDraft(current.Value));
                break;
            }
            if (state.LoopExitTargets.TryPeek(out var loopExit) && current == loopExit)
            {
                regions.Add(new StructuredLoopBreakDraft());
                break;
            }
            if (state.LoopContinueTargets.TryPeek(out var loopContinue) &&
                current == loopContinue && path.Count != 0)
            {
                regions.Add(new StructuredLoopContinueDraft());
                break;
            }
            if (!allowed.Contains(current.Value))
            {
                break;
            }
            // Every reachable cycle is claimed by the natural-loop or SCC
            // analysis before sequence traversal begins. Keep the path set
            // solely for loop-continue recognition.

            path.Add(current.Value);

            var sequenceStep = _sequenceSteps.Execute(
                this, state, current.Value, stop, allowed, path, regions);
            if (sequenceStep.Disposition == StructuredSequenceStepDisposition.Continue)
            {
                current = sequenceStep.NextBlockOffset;
                continue;
            }


            var block = state.Graph.GetBlock(current.Value);
            var successors = state.Graph.Successors[current.Value];
            if (state.BoundaryOwnedBlocks.Contains(block.Index))
            {
                state.BoundaryClaimedBlockBodies.Add(block.Index);
            }

            regions.Add(new StructuredBlockDraft(
                block,
                !state.ContinuationOwnedBlocks.Contains(block.Index) &&
                state.ClaimedBlockBodies.Add(block.Index)));
            current = successors.Length == 0 ? null : successors[0];
        }
        return new StructuredSequenceDraft(regions.ToImmutable());
    }
    public StructuredDispatcherDraft Build(ControlFlowStructuringState state,
        int? entry,
        ImmutableHashSet<int> component,
        ImmutableHashSet<int> allowed,
        int? exitStop,
        HashSet<int> path)
    {
        var shellBuild = _exceptionAwareDispatcherShells.Build(state, entry, component, allowed, exitStop);
        var shell = shellBuild.Dispatcher;
        state.ActiveDispatchers.Push(shellBuild.OwnedBlocks);
        try
        {
            return shell with
            {
                Exits = [.. shell.Exits.Select(exit =>
                    new StructuredDispatcherExitDraft(
                        exit.TargetBlock,
                        Build(state,
                            exit.TargetBlock,
                            stop: exitStop,
                            allowed,
                            [.. path])))],
            };
        }
        finally
        {
            state.ActiveDispatchers.Pop();
        }
    }
}
