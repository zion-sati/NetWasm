using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Structured;

internal sealed class StructuredTargetValidator : IStructuredTargetValidator
{
    public void Validate(StructuredMethod method)
    {
        ArgumentNullException.ThrowIfNull(method);
        RequireBlock(method.EntryBlock, method);
        foreach (var (key, block) in method.Blocks)
        {
            if (key != block.Id) throw new InvalidOperationException("A structured block key does not match its identity.");
            ValidateExit(block.Exit, method);
        }
        Visit(method.Body, method, activeGroup: null, loopDepth: 0, dispatcherDepth: 0, [], labels: []);
    }

    private static void ValidateExit(StructuredBlockExit exit, StructuredMethod method)
    {
        switch (exit)
        {
            case StructuredFallthroughExit { Target: { } target }:
                RequireBlock(target, method);
                break;
            case StructuredBranchExit branch:
                RequireBlock(branch.Target, method);
                break;
            case StructuredConditionalExit conditional:
                RequireBlock(conditional.WhenTaken, method);
                RequireBlock(conditional.WhenNotTaken, method);
                break;
            case StructuredLeaveExit leave:
                RequireBlock(leave.Target, method);
                break;
            case StructuredTerminalExit terminal when terminal.Instruction.Operation is not (
                    CilOperation.Return or CilOperation.Throw or CilOperation.Rethrow or
                    CilOperation.EndFinally or CilOperation.EndFilter):
                throw new InvalidOperationException("A structured terminal exit contains a non-terminal operation.");
        }
    }

    private static void Visit(
        StructuredSequence sequence,
        StructuredMethod method,
        StructuredExceptionGroupId? activeGroup,
        int loopDepth,
        int dispatcherDepth,
        HashSet<(
            StructuredExceptionGroupId Group,
            StructuredContinuationId? FallthroughContinuation,
            string DispatcherContinuations,
            bool HasLoop,
            bool HasDispatcher)> visitedGroups,
        ImmutableHashSet<StructuredBlockId> labels)
    {
        foreach (var region in sequence.Regions)
        {
            switch (region)
            {
                case StructuredCode code:
                    ValidateOccurrence(code.Occurrence, method, activeGroup, allowConditional: false);
                    break;
                case StructuredLoopBreak when loopDepth == 0:
                    throw new InvalidOperationException("A structured loop break has no enclosing loop.");
                case StructuredLoopContinue when loopDepth == 0:
                    throw new InvalidOperationException("A structured loop continuation has no enclosing loop.");
                case StructuredDispatcherContinue continuation:
                    if (dispatcherDepth == 0)
                        throw new InvalidOperationException("A dispatcher continuation has no enclosing dispatcher.");
                    RequireBlock(continuation.Target, method);
                    break;
                case StructuredExceptionRegion exception:
                    if (!method.ExceptionGroups.ContainsKey(exception.Group))
                        throw new InvalidOperationException("A structured exception region references an unknown group.");
                    ValidateContinuation(exception.FallthroughContinuation, exception.Group, method);
                    foreach (var continuation in exception.DispatcherContinuations)
                    {
                        ValidateContinuation(continuation, exception.Group, method);
                    }
                    if (exception.DispatcherContinuations.Count != 0 && dispatcherDepth == 0)
                    {
                        throw new InvalidOperationException(
                            "An exception continuation routes through a dispatcher " +
                            "without an enclosing dispatcher.");
                    }
                    Visit(
                        exception.Group,
                        exception.FallthroughContinuation,
                        exception.DispatcherContinuations,
                        method,
                        visitedGroups,
                        loopDepth,
                        dispatcherDepth);
                    break;
                case StructuredIf conditional:
                    ValidateOccurrence(conditional.Condition, method, activeGroup, allowConditional: true);
                    RequireConditional(conditional.Condition.Block, method);
                    Visit(conditional.WhenTrue, method, activeGroup, loopDepth, dispatcherDepth, visitedGroups, labels);
                    Visit(conditional.WhenFalse, method, activeGroup, loopDepth, dispatcherDepth, visitedGroups, labels);
                    break;
                case StructuredLoop loop:
                    if (loop.Condition is { } condition)
                    {
                        ValidateOccurrence(condition, method, activeGroup, allowConditional: true);
                        RequireConditional(condition.Block, method);
                    }
                    else if (!loop.ContinueBody.Regions.IsEmpty || !loop.ExitBody.Regions.IsEmpty)
                    {
                        throw new InvalidOperationException("An unconditional structured loop has a continuation or exit body.");
                    }
                    Visit(loop.Body, method, activeGroup, loopDepth + 1, dispatcherDepth, visitedGroups, labels);
                    Visit(loop.ContinueBody, method, activeGroup, loopDepth + 1, dispatcherDepth, visitedGroups, labels);
                    Visit(loop.ExitBody, method, activeGroup, loopDepth, dispatcherDepth, visitedGroups, labels);
                    break;
                case StructuredPostTestLoop loop:
                    ValidateOccurrence(loop.Condition, method, activeGroup, allowConditional: true);
                    RequireConditional(loop.Condition.Block, method);
                    Visit(loop.Body, method, activeGroup, loopDepth + 1, dispatcherDepth, visitedGroups, labels);
                    Visit(loop.ContinueBody, method, activeGroup, loopDepth + 1, dispatcherDepth, visitedGroups, labels);
                    Visit(loop.ExitBody, method, activeGroup, loopDepth, dispatcherDepth, visitedGroups, labels);
                    break;
                case StructuredLabeledBlock block:
                    // Leaving the body's end reaches Next's code, which follows.
                    RequireBlock(block.Next, method);
                    Visit(block.Body, method, activeGroup, loopDepth, dispatcherDepth, visitedGroups, labels.Add(block.Next));
                    break;
                case StructuredLabeledLoop loop:
                    RequireBlock(loop.Header, method);
                    RequireTransferAtEnd(loop.Body, method);
                    Visit(loop.Body, method, activeGroup, loopDepth, dispatcherDepth, visitedGroups, labels.Add(loop.Header));
                    break;
                case StructuredLabelBranch branch when !labels.Contains(branch.Target):
                    throw new InvalidOperationException(
                        "A structured label branch has no enclosing labeled block or loop for its target.");
                case StructuredDispatcher dispatcher:
                    ValidateDispatcher(dispatcher, method, activeGroup, loopDepth, dispatcherDepth, visitedGroups);
                    break;
            }
        }
    }

    private static void Visit(
        StructuredExceptionGroupId groupId,
        StructuredContinuationId? fallthroughContinuation,
        IReadOnlySet<StructuredContinuationId> dispatcherContinuations,
        StructuredMethod method,
        HashSet<(
            StructuredExceptionGroupId Group,
            StructuredContinuationId? FallthroughContinuation,
            string DispatcherContinuations,
            bool HasLoop,
            bool HasDispatcher)> visitedGroups,
        int loopDepth,
        int dispatcherDepth)
    {
        if (!visitedGroups.Add((
                groupId,
                fallthroughContinuation,
                string.Join(',', dispatcherContinuations
                    .Select(continuation => continuation.Value)
                    .Order()),
                loopDepth > 0,
                dispatcherDepth > 0)))
        {
            return;
        }
        var group = method.ExceptionGroups[groupId];
        foreach (var part in group.ProtectedParts)
        {
            switch (part)
            {
                case StructuredExceptionCode code:
                    Visit(code.Body, method, groupId, 0, 0, visitedGroups, labels: []);
                    break;
                case StructuredNestedExceptionGroup nested:
                    if (!method.ExceptionGroups.ContainsKey(nested.Group))
                        throw new InvalidOperationException("A nested exception part references an unknown group.");
                    Visit(nested.Group, null, new HashSet<StructuredContinuationId>(),
                        method, visitedGroups, 0, 0);
                    break;
            }
        }
        foreach (var clause in group.Clauses)
        {
            if (clause.FilterBody is not null)
                Visit(clause.FilterBody, method, groupId, 0, 0, visitedGroups, labels: []);
            Visit(clause.HandlerBody, method, groupId, 0, 0, visitedGroups, labels: []);
        }
        if (group.ContinuationDispatcher is null)
        {
            foreach (var continuation in group.NormalContinuations)
            {
                if (continuation.Id == fallthroughContinuation ||
                    dispatcherContinuations.Contains(continuation.Id) ||
                    RoutesThroughParent(group, continuation, method))
                {
                    continue;
                }
                Visit(continuation.Body, method, group.Parent, loopDepth, 0, visitedGroups, labels: []);
            }
        }
        if (group.ContinuationDispatcher is not null)
            ValidateDispatcher(
                group.ContinuationDispatcher,
                method,
                group.Parent,
                loopDepth,
                dispatcherDepth,
                visitedGroups);
    }

    private static void ValidateDispatcher(
        StructuredDispatcher dispatcher,
        StructuredMethod method,
        StructuredExceptionGroupId? activeGroup,
        int loopDepth,
        int dispatcherDepth,
        HashSet<(
            StructuredExceptionGroupId Group,
            StructuredContinuationId? FallthroughContinuation,
            string DispatcherContinuations,
            bool HasLoop,
            bool HasDispatcher)> visitedGroups)
    {
        var blocks = dispatcher.Blocks.Select(block => block.Occurrence.Block).ToHashSet();
        var exits = dispatcher.Exits.Select(exit => exit.Target).ToHashSet();
        if (dispatcher.EntryBlock is { } entry && !blocks.Contains(entry))
            throw new InvalidOperationException("A dispatcher entry does not reference one of its blocks.");
        foreach (var block in dispatcher.Blocks)
        {
            ValidateOccurrence(block.Occurrence, method, activeGroup, allowConditional: true);
            ValidateDispatcherTarget(block.WhenTrue, blocks, exits);
            ValidateDispatcherTarget(block.WhenFalse, blocks, exits);
        }
        foreach (var exit in dispatcher.Exits)
        {
            RequireBlock(exit.Target, method);
            Visit(exit.Body, method, activeGroup, loopDepth, dispatcherDepth + 1, visitedGroups, labels: []);
        }
    }

    // Control leaving a labeled loop's end would leave the loop, so every
    // path through its body ends in a transfer.
    private static void RequireTransferAtEnd(StructuredSequence sequence, StructuredMethod method)
    {
        if (!EndsInTransfer(sequence, method))
            throw new InvalidOperationException("A labeled structured body can reach its end.");
    }

    private static bool EndsInTransfer(StructuredSequence sequence, StructuredMethod method) =>
        !sequence.Regions.IsEmpty && sequence.Regions[^1] switch
        {
            StructuredLabelBranch or StructuredLoopBreak or StructuredLoopContinue or
                StructuredDispatcherContinue or StructuredLabeledLoop => true,
            StructuredCode code => method.Blocks[code.Occurrence.Block].Exit is StructuredTerminalExit,
            StructuredIf conditional =>
                EndsInTransfer(conditional.WhenTrue, method) && EndsInTransfer(conditional.WhenFalse, method),
            // A break would leave it at its end; labeled structure has none.
            StructuredLoop { Condition: not null } loop =>
                EndsInTransfer(loop.ExitBody, method) && !ContainsBreak(loop.Body) && !ContainsBreak(loop.ContinueBody),
            _ => false,
        };

    private static bool ContainsBreak(StructuredSequence sequence) => sequence.Regions.Any(region => region switch
    {
        StructuredLoopBreak => true,
        StructuredIf conditional => ContainsBreak(conditional.WhenTrue) || ContainsBreak(conditional.WhenFalse),
        StructuredLabeledBlock labeled => ContainsBreak(labeled.Body),
        StructuredLabeledLoop labeled => ContainsBreak(labeled.Body),
        _ => false,
    });

    private static void ValidateDispatcherTarget(
        StructuredBlockId? target,
        HashSet<StructuredBlockId> blocks,
        HashSet<StructuredBlockId> exits)
    {
        if (target is { } value && !blocks.Contains(value) && !exits.Contains(value))
            throw new InvalidOperationException("A dispatcher transition has no block or exit.");
    }

    private static bool RoutesThroughParent(
        StructuredExceptionGroup group,
        StructuredExceptionContinuation continuation,
        StructuredMethod method) =>
        group.Parent is { } parent &&
        method.ExceptionGroups[parent].NormalContinuations.Any(candidate =>
            candidate.Target == continuation.Target);

    private static void ValidateOccurrence(
        StructuredBlockOccurrence occurrence,
        StructuredMethod method,
        StructuredExceptionGroupId? activeGroup,
        bool allowConditional)
    {
        var block = RequireBlock(occurrence.Block, method);
        if (!allowConditional && block.Exit is StructuredConditionalExit)
            throw new InvalidOperationException("A conditional block is not represented by a structured condition.");
        if (block.Exit is not StructuredLeaveExit leave)
        {
            if (occurrence.LeaveContinuation is not null)
                throw new InvalidOperationException("A non-leave block carries a leave continuation.");
            return;
        }
        if (occurrence.Role == StructuredBlockRole.RoutingReplica)
        {
            if (occurrence.LeaveContinuation is not null)
                throw new InvalidOperationException("A routing-only leave replica carries a continuation.");
            return;
        }
        if (activeGroup is null || occurrence.LeaveContinuation is null)
            throw new InvalidOperationException(
                $"Executing leave block {occurrence.Block.Value} ({occurrence.Role}) has no active structured continuation.");
        var continuation = method.ExceptionGroups[activeGroup.Value].NormalContinuations
            .SingleOrDefault(candidate => candidate.Id == occurrence.LeaveContinuation.Value)
            ?? throw new InvalidOperationException("An executing leave block references an unknown continuation.");
        if (continuation.Target != leave.Target)
            throw new InvalidOperationException("An executing leave continuation targets a different block.");
    }

    private static void ValidateContinuation(
        StructuredContinuationId? continuation,
        StructuredExceptionGroupId group,
        StructuredMethod method)
    {
        if (continuation is { } value &&
            !method.ExceptionGroups[group].NormalContinuations.Any(candidate => candidate.Id == value))
        {
            throw new InvalidOperationException("An exception fallthrough references an unknown continuation.");
        }
    }

    private static void RequireConditional(StructuredBlockId id, StructuredMethod method)
    {
        if (RequireBlock(id, method).Exit is not StructuredConditionalExit)
            throw new InvalidOperationException("A structured condition references a non-conditional block.");
    }

    private static StructuredBlockDefinition RequireBlock(StructuredBlockId id, StructuredMethod method) =>
        method.Blocks.TryGetValue(id, out var block)
            ? block
            : throw new InvalidOperationException("Structured control flow references an unknown block.");

}
