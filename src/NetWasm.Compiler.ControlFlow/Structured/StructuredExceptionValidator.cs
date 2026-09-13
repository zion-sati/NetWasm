using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Structured;

internal sealed class StructuredExceptionValidator : IStructuredExceptionValidator
{
    public void Validate(StructuredMethod method)
    {
        ArgumentNullException.ThrowIfNull(method);
        if (method.TopLevelExceptionGroups.Distinct().Count() != method.TopLevelExceptionGroups.Length)
            throw new InvalidOperationException("A top-level exception group is listed more than once.");
        foreach (var topLevel in method.TopLevelExceptionGroups)
        {
            if (!method.ExceptionGroups.TryGetValue(topLevel, out var topLevelGroup) || topLevelGroup.Parent is not null)
                throw new InvalidOperationException("A top-level exception identity is unknown or has a parent.");
        }
        foreach (var (key, group) in method.ExceptionGroups)
        {
            if (key != group.Id) throw new InvalidOperationException("An exception-group key does not match its identity.");
            ValidateGroupFacts(group, method);
        }

        var roots = FindRootGroups(method.Body).Distinct().ToArray();
        var visited = new HashSet<StructuredExceptionGroupId>();
        foreach (var root in roots) Visit(root, null, method, visited);
        if (!visited.SetEquals(method.ExceptionGroups.Keys))
            throw new InvalidOperationException("The structured method contains an unreachable exception group.");
    }

    private static void ValidateGroupFacts(StructuredExceptionGroup group, StructuredMethod method)
    {
        if (group.TryOffset < 0 || group.TryLength <= 0)
            throw new InvalidOperationException("An exception group has an invalid protected source range.");
        if (group.ProtectedBlocks.IsDefaultOrEmpty ||
            group.ProtectedBlocks.Distinct().Count() != group.ProtectedBlocks.Length ||
            group.ProtectedBlocks.Any(block =>
                !method.Blocks.TryGetValue(block, out var definition) ||
                definition.StartOffset < group.TryOffset ||
                definition.StartOffset >= group.TryOffset + group.TryLength))
        {
            throw new InvalidOperationException(
                "An exception group has inconsistent protected block facts.");
        }
        var continuationIds = new HashSet<StructuredContinuationId>();
        foreach (var continuation in group.NormalContinuations)
        {
            if (!continuationIds.Add(continuation.Id))
                throw new InvalidOperationException("An exception group contains a duplicate continuation identity.");
            if (!method.Blocks.TryGetValue(continuation.Target, out var target) ||
                target.StartOffset != continuation.TargetOffset)
            {
                throw new InvalidOperationException("An exception continuation has an inconsistent target.");
            }
        }
        if (group.ContinuationJoinBlock is { } join && !method.Blocks.ContainsKey(join))
            throw new InvalidOperationException("An exception continuation join references an unknown block.");
        foreach (var clause in group.Clauses) ValidateClause(clause, method);
    }

    private static void ValidateClause(
        StructuredExceptionClause clause,
        StructuredMethod method)
    {
        if (clause.HandlerOffset < 0 || clause.HandlerLength <= 0)
            throw new InvalidOperationException("An exception clause has an invalid handler source range.");
        if (clause.Kind == CilExceptionRegionKind.Catch && clause.CatchType is null)
            throw new InvalidOperationException("A catch clause has no catch type.");
        if (clause.Kind != CilExceptionRegionKind.Catch && clause.CatchType is not null)
            throw new InvalidOperationException("A non-catch clause carries a catch type.");
        if (!method.Blocks.TryGetValue(clause.HandlerBlock, out var handler) ||
            handler.StartOffset != clause.HandlerOffset)
        {
            throw new InvalidOperationException(
                "An exception clause has an inconsistent handler block.");
        }
        if (clause.Kind == CilExceptionRegionKind.Filter)
        {
            if (clause.FilterOffset is not int filterOffset ||
                clause.FilterBody is null ||
                clause.FilterBlock is not { } filterBlock ||
                !method.Blocks.TryGetValue(filterBlock, out var filter) ||
                filter.StartOffset != filterOffset)
                throw new InvalidOperationException("A filter clause has no filter source and body.");
        }
        else if (clause.FilterOffset is not null ||
                 clause.FilterBody is not null ||
                 clause.FilterBlock is not null)
        {
            throw new InvalidOperationException("A non-filter clause carries filter facts.");
        }
    }

    private static void Visit(
        StructuredExceptionGroupId id,
        StructuredExceptionGroupId? expectedParent,
        StructuredMethod method,
        HashSet<StructuredExceptionGroupId> visited)
    {
        if (!method.ExceptionGroups.TryGetValue(id, out var group))
            throw new InvalidOperationException("Structured control flow references an unknown exception group.");
        if (group.Parent != expectedParent)
            throw new InvalidOperationException("A structured exception group has an inconsistent parent.");
        if (visited.Contains(id)) return;
        foreach (var (referenced, parent) in ReferencedGroups(group))
            Visit(referenced, parent, method, visited);
        visited.Add(id);
    }

    private static IEnumerable<(StructuredExceptionGroupId Group, StructuredExceptionGroupId? Parent)> ReferencedGroups(
        StructuredExceptionGroup group)
    {
        foreach (var nested in group.ProtectedParts.OfType<StructuredNestedExceptionGroup>())
            yield return (nested.Group, group.Id);
        foreach (var code in group.ProtectedParts.OfType<StructuredExceptionCode>())
            foreach (var referenced in FindRootGroups(code.Body)) yield return (referenced, group.Id);
        foreach (var clause in group.Clauses)
        {
            foreach (var referenced in FindRootGroups(clause.HandlerBody)) yield return (referenced, group.Id);
            if (clause.FilterBody is not null)
                foreach (var referenced in FindRootGroups(clause.FilterBody)) yield return (referenced, group.Id);
        }
        foreach (var continuation in group.NormalContinuations)
            foreach (var referenced in FindRootGroups(continuation.Body)) yield return (referenced, group.Parent);
        if (group.ContinuationDispatcher is not null)
        {
            foreach (var exit in group.ContinuationDispatcher.Exits)
                foreach (var referenced in FindRootGroups(exit.Body)) yield return (referenced, group.Parent);
        }
    }

    private static IEnumerable<StructuredExceptionGroupId> FindRootGroups(StructuredSequence sequence)
    {
        foreach (var region in sequence.Regions)
        {
            switch (region)
            {
                case StructuredExceptionRegion exception:
                    yield return exception.Group;
                    break;
                case StructuredIf conditional:
                    foreach (var group in FindRootGroups(conditional.WhenTrue)) yield return group;
                    foreach (var group in FindRootGroups(conditional.WhenFalse)) yield return group;
                    break;
                case StructuredLoop loop:
                    foreach (var group in FindRootGroups(loop.Body)) yield return group;
                    foreach (var group in FindRootGroups(loop.ContinueBody)) yield return group;
                    foreach (var group in FindRootGroups(loop.ExitBody)) yield return group;
                    break;
                case StructuredPostTestLoop loop:
                    foreach (var group in FindRootGroups(loop.Body)) yield return group;
                    foreach (var group in FindRootGroups(loop.ContinueBody)) yield return group;
                    foreach (var group in FindRootGroups(loop.ExitBody)) yield return group;
                    break;
                case StructuredDispatcher dispatcher:
                    foreach (var exit in dispatcher.Exits)
                        foreach (var group in FindRootGroups(exit.Body)) yield return group;
                    break;
            }
        }
    }
}
