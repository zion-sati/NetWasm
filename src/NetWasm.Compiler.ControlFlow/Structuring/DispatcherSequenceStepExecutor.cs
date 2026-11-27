// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

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
            var withinAllowed = loop.BodyComponent.All(allowed.Contains);
            var retainedActiveBlocks = withinAllowed
                ? _activeBlockRetention.Select(state, loop)
                : ImmutableHashSet<int>.Empty;
            var loopAllowed = loop.BodyComponent.Within(allowed);
            // Natural-loop analysis reports a continue target in the
            // structured domain; retain it in the clipped body set.
            var effectiveContinue = loop.Continue;
            loopAllowed = loopAllowed.Add(effectiveContinue);
            var clipsLoopBoundary = !withinAllowed;
            if (loop.Condition is not int condition)
            {
                regions.Add(BuildUnconditionalLoop(
                    structuredControlFlowBuilder,
                    state,
                    loop,
                    loopAllowed,
                    clipsLoopBoundary,
                    path,
                    retainedActiveBlocks));
                return new StructuredSequenceStepResult(
                    StructuredSequenceStepDisposition.Continue,
                    loop.Exit);
            }
            var conditionBlock = state.Graph.GetBlock(condition);
            var conditionIsOriginal = state.ClaimedBlockBodies.Add(conditionBlock.Index);
            if (clipsLoopBoundary)
            {
                state.BoundaryOwnedBlocks.UnionWith(loop.BodyComponent);
                state.BoundaryOwnedBlocks.UnionWith(loop.ConditionExitComponent);
                state.BoundaryClaimedBlockBodies.Add(conditionBlock.Index);
            }
            // A loop structured a second time (a replica, where enclosing branches reach it twice) clips by the active
            // path, keeping its own blocks as a loop inside the caller's blocks does: the boundary-claimed bodies hold
            // every block the first emitted, which would leave it without a body.
            var activeBlocks = clipsLoopBoundary && conditionIsOriginal
                ? state.BoundaryClaimedBlockBodies
                : path;
            if (clipsLoopBoundary && !conditionIsOriginal)
            {
                retainedActiveBlocks = _activeBlockRetention.Select(state, loop);
            }
            var target = state.Graph.Successors[condition][0];
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
            if (condition == loop.Header)
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
                    loop.ConditionExitComponent.Within(allowed),
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
                StructuredSequenceDraft continueBody;
                try
                {
                    loopBody = structuredControlFlowBuilder.Build(state,
                        loop.Header,
                        condition,
                _allowedBlocks.Clip(
                    loopAllowed,
                    activeBlocks,
                    retainedActiveBlocks),
                []);
                    // The continuation is still inside this loop. Its early
                    // exits must retain this loop's break target and may run
                    // the condition-exit extension before reaching that target.
                    continueBody = structuredControlFlowBuilder.Build(state,
                        state.Graph.Successors[condition]
                            .Single(loop.CoreComponent.Contains),
                        loop.Header,
                        _allowedBlocks.Clip(
                            loopAllowed.Union(loop.ConditionExitComponent.Within(allowed)),
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
                    continueBody,
                    structuredControlFlowBuilder.Build(state,
                        loop.ConditionExit,
                        loop.Exit,
                _allowedBlocks.Clip(
                    loop.ConditionExitComponent.Within(allowed),
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

    private StructuredLoopDraft BuildUnconditionalLoop(
        IStructuredControlFlowBuilder structuredControlFlowBuilder,
        ControlFlowStructuringState state,
        LoopRegion loop,
        ImmutableHashSet<int> loopAllowed,
        bool clipsLoopBoundary,
        HashSet<int> path,
        ImmutableHashSet<int> retainedActiveBlocks)
    {
        // The body runs from the header until a break, continue, return or an
        // exception region's continuation leaves it; reaching its end repeats it. A loop structured a second time (a
        // replica, its header already emitted) clips by the active path, as a conditional loop's replica does: the
        // boundary-claimed bodies hold every block the first emitted, which would leave it an empty, endless loop.
        var isOriginal = !state.ClaimedBlockBodies.Contains(loop.Header);
        var active = clipsLoopBoundary && isOriginal ? state.BoundaryClaimedBlockBodies : (IReadOnlySet<int>)path;
        var retained = clipsLoopBoundary && !isOriginal
            ? _activeBlockRetention.Select(state, loop)
            : retainedActiveBlocks;
        if (clipsLoopBoundary)
        {
            state.BoundaryOwnedBlocks.UnionWith(loop.BodyComponent);
        }
        state.SuppressedLoopHeaders.Add(loop.Header);
        state.LoopExitTargets.Push(loop.Exit);
        state.LoopContinueTargets.Push(loop.Continue);
        try
        {
            return new StructuredLoopDraft(
                null,
                true,
                structuredControlFlowBuilder.Build(state,
                    loop.Header,
                    stop: null,
                    _allowedBlocks.Clip(loopAllowed, active, retained),
                    []),
                StructuredSequenceDraft.Empty,
                StructuredSequenceDraft.Empty);
        }
        finally
        {
            state.LoopContinueTargets.Pop();
            state.LoopExitTargets.Pop();
            state.SuppressedLoopHeaders.Remove(loop.Header);
        }
    }
}
