using NetWasm.Compiler.ControlFlow.Draft;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal sealed class DispatcherSequenceStepExecutor(
    IActiveControlFlowBlockClipper allowedBlocks,
    ILoopActiveBlockRetentionSelector activeBlockRetention)
    : IStructuredSequenceStepExecutor
{
    private readonly IActiveControlFlowBlockClipper _allowedBlocks =
        allowedBlocks ?? throw new ArgumentNullException(nameof(allowedBlocks));
    private readonly ILoopActiveBlockRetentionSelector _activeBlockRetention =
        activeBlockRetention ?? throw new ArgumentNullException(nameof(activeBlockRetention));
    public StructuredSequenceStepResult Execute(
        IStructuredControlFlowBuilder structuredControlFlowBuilder,
        ControlFlowStructuringState state,
        int currentBlockOffset,
        int? stop,
        ImmutableHashSet<int> allowed,
        HashSet<int> path,
        ImmutableArray<StructuredRegionDraft>.Builder regions)
    {
        int? current = currentBlockOffset;

        if (!state.SuppressedLoopHeaders.Contains(current.Value) &&
            !state.DispatchersByNode.ContainsKey(current.Value) &&
            state.LoopsByHeader.TryGetValue(current.Value, out var loop))
        {
            // A whole-method natural loop may include an exit extension used to
            // reunify early breaks. When the loop is being structured inside an
            // exception region, that extension can cross the region boundary.
            // Keep the loop body inside the caller's allowed block set so a
            // `leave` records its continuation and cleanup runs before the
            // outside continuation is emitted.
            var retainedActiveBlocks = loop.BodyComponent.IsSubsetOf(allowed)
                ? _activeBlockRetention.Select(state, loop)
                : ImmutableHashSet<int>.Empty;
            var loopAllowed = loop.BodyComponent.Intersect(allowed);
            // Natural-loop analysis reports a continue target in the
            // structured domain; retain it in the clipped body set.
            var effectiveContinue = loop.Continue;
            loopAllowed = loopAllowed.Add(effectiveContinue);
            var conditionBlock = state.Graph.GetBlock(loop.Condition);
            var clipsLoopBoundary = !loop.BodyComponent.IsSubsetOf(allowed);
            var conditionIsOriginal = state.ClaimedBlockBodies.Add(conditionBlock.Index);
            if (clipsLoopBoundary)
            {
                state.BoundaryOwnedBlocks.UnionWith(loop.BodyComponent);
                state.BoundaryOwnedBlocks.UnionWith(loop.ConditionExitComponent);
                state.BoundaryClaimedBlockBodies.Add(conditionBlock.Index);
            }
            var activeBlocks = clipsLoopBoundary
                ? state.BoundaryClaimedBlockBodies
                : path;
            var target = state.Graph.Successors[loop.Condition][0];
            var branchSelectsBody = loop.CoreComponent.Contains(target);
            // CreateLoop establishes that the condition has a conditional branch
            // and fallthrough successors, exactly one of which remains in the loop.
            var continueWhenTrue =
                conditionBlock.Terminator.Operation is CilOperation.BranchIfTrue or
                    CilOperation.BranchIfEqual or CilOperation.BranchIfNotEqual or
                    CilOperation.BranchIfGreaterThanSigned or
                    CilOperation.BranchIfGreaterThanUnsigned or
                    CilOperation.BranchIfGreaterThanOrEqualSigned or
                    CilOperation.BranchIfGreaterThanOrEqualUnsigned or
                    CilOperation.BranchIfLessThanSigned or
                    CilOperation.BranchIfLessThanUnsigned or
                    CilOperation.BranchIfLessThanOrEqualSigned or
                    CilOperation.BranchIfLessThanOrEqualUnsigned
                    ? branchSelectsBody
                    : !branchSelectsBody;
            if (loop.Condition == loop.Header)
            {
                var bodyStart = state.Graph.Successors[loop.Header]
                    .Single(loop.CoreComponent.Contains);
                state.LoopExitTargets.Push(loop.Exit);
                state.LoopContinueTargets.Push(effectiveContinue);
                StructuredSequenceDraft loopBody;
                try
                {
                    loopBody = structuredControlFlowBuilder.Build(state,
                        bodyStart,
                        effectiveContinue,
                _allowedBlocks.Clip(
                    loopAllowed,
                    activeBlocks,
                    retainedActiveBlocks),
                []);
                }
                finally
                {
                    state.LoopContinueTargets.Pop();
                    state.LoopExitTargets.Pop();
                }
                regions.Add(new StructuredLoopDraft(
                    conditionBlock,
                    continueWhenTrue,
                    loopBody,
                    structuredControlFlowBuilder.Build(state,
                        effectiveContinue,
                        loop.Header,
                _allowedBlocks.Clip(
                    loopAllowed,
                    activeBlocks,
                    retainedActiveBlocks),
                []),
                    structuredControlFlowBuilder.Build(state,
                        loop.ConditionExit,
                        loop.Exit,
                _allowedBlocks.Clip(
                    loop.ConditionExitComponent.Intersect(allowed),
                    activeBlocks,
                        retainedActiveBlocks),
                []))
                {
                    IsOriginal = conditionIsOriginal,
                });
            }
            else
            {
                state.SuppressedLoopHeaders.Add(loop.Header);
                state.LoopExitTargets.Push(loop.Exit);
                state.LoopContinueTargets.Push(loop.Continue);
                StructuredSequenceDraft loopBody;
                try
                {
                    loopBody = structuredControlFlowBuilder.Build(state,
                        loop.Header,
                        loop.Condition,
                _allowedBlocks.Clip(
                    loopAllowed,
                    activeBlocks,
                    retainedActiveBlocks),
                []);
                }
                finally
                {
                    state.LoopContinueTargets.Pop();
                    state.SuppressedLoopHeaders.Remove(loop.Header);
                    state.LoopExitTargets.Pop();
                }
                regions.Add(new StructuredPostTestLoopDraft(
                    loopBody,
                    conditionBlock,
                    continueWhenTrue,
                    structuredControlFlowBuilder.Build(state,
                        state.Graph.Successors[loop.Condition]
                            .Single(loop.CoreComponent.Contains),
                        loop.Header,
                _allowedBlocks.Clip(
                    loopAllowed,
                    activeBlocks,
                    retainedActiveBlocks),
                []),
                    structuredControlFlowBuilder.Build(state,
                        loop.ConditionExit,
                        loop.Exit,
                _allowedBlocks.Clip(
                    loop.ConditionExitComponent.Intersect(allowed),
                    activeBlocks,
                        retainedActiveBlocks),
                []))
                {
                    IsOriginal = conditionIsOriginal,
                });
            }
            current = loop.Exit;
            return new StructuredSequenceStepResult(
                StructuredSequenceStepDisposition.Continue,
                current);
        }

        return new StructuredSequenceStepResult(
            StructuredSequenceStepDisposition.NotHandled,
            current);
    }
}
