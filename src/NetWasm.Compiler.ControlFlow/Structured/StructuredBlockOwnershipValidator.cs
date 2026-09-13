using System;
using System.Collections.Generic;
using System.Linq;

namespace NetWasm.Compiler.ControlFlow.Structured;

internal sealed class StructuredBlockOwnershipValidator : IStructuredBlockOwnershipValidator
{
    public void Validate(StructuredMethod method)
    {
        ArgumentNullException.ThrowIfNull(method);
        var owners = new Dictionary<StructuredBlockId, int>();
        var visitedGroups = new HashSet<StructuredExceptionGroupId>();
        Visit(method.Body, method, owners, visitedGroups);
        var invalid = method.Blocks.Keys
            .Where(block => owners.GetValueOrDefault(block) != 1)
            .ToArray();
        if (invalid.Length != 0)
        {
            throw new InvalidOperationException(
                $"Structured blocks require exactly one owner; invalid count: {invalid.Length}.");
        }
    }

    private static void Visit(
        StructuredSequence sequence,
        StructuredMethod method,
        Dictionary<StructuredBlockId, int> owners,
        HashSet<StructuredExceptionGroupId> visitedGroups)
    {
        foreach (var region in sequence.Regions)
        {
            switch (region)
            {
                case StructuredCode code:
                    Record(code.Occurrence, owners);
                    break;
                case StructuredIf conditional:
                    Record(conditional.Condition, owners);
                    Visit(conditional.WhenTrue, method, owners, visitedGroups);
                    Visit(conditional.WhenFalse, method, owners, visitedGroups);
                    break;
                case StructuredLoop loop:
                    Record(loop.Condition, owners);
                    Visit(loop.Body, method, owners, visitedGroups);
                    Visit(loop.ContinueBody, method, owners, visitedGroups);
                    Visit(loop.ExitBody, method, owners, visitedGroups);
                    break;
                case StructuredPostTestLoop loop:
                    Visit(loop.Body, method, owners, visitedGroups);
                    Record(loop.Condition, owners);
                    Visit(loop.ContinueBody, method, owners, visitedGroups);
                    Visit(loop.ExitBody, method, owners, visitedGroups);
                    break;
                case StructuredDispatcher dispatcher:
                    foreach (var block in dispatcher.Blocks) Record(block.Occurrence, owners);
                    foreach (var exit in dispatcher.Exits) Visit(exit.Body, method, owners, visitedGroups);
                    break;
                case StructuredExceptionRegion exception:
                    Visit(exception.Group, method, owners, visitedGroups);
                    break;
            }
        }
    }

    private static void Visit(
        StructuredExceptionGroupId groupId,
        StructuredMethod method,
        Dictionary<StructuredBlockId, int> owners,
        HashSet<StructuredExceptionGroupId> visitedGroups)
    {
        if (!visitedGroups.Add(groupId) || !method.ExceptionGroups.TryGetValue(groupId, out var group)) return;
        foreach (var part in group.ProtectedParts)
        {
            switch (part)
            {
                case StructuredExceptionCode code:
                    Visit(code.Body, method, owners, visitedGroups);
                    break;
                case StructuredNestedExceptionGroup nested:
                    Visit(nested.Group, method, owners, visitedGroups);
                    break;
            }
        }
        foreach (var clause in group.Clauses)
        {
            if (clause.FilterBody is not null) Visit(clause.FilterBody, method, owners, visitedGroups);
            Visit(clause.HandlerBody, method, owners, visitedGroups);
        }
        foreach (var continuation in group.NormalContinuations)
            Visit(continuation.Body, method, owners, visitedGroups);
        if (group.ContinuationDispatcher is not null)
            Visit(new StructuredSequence([group.ContinuationDispatcher]), method, owners, visitedGroups);
    }

    private static void Record(
        StructuredBlockOccurrence occurrence,
        Dictionary<StructuredBlockId, int> owners)
    {
        if (occurrence.Role == StructuredBlockRole.Owner)
            owners[occurrence.Block] = owners.GetValueOrDefault(occurrence.Block) + 1;
    }
}
