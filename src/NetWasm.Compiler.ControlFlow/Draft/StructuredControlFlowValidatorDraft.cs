using System;
using System.Collections.Generic;
using System.Linq;

namespace NetWasm.Compiler.ControlFlow.Draft;

internal sealed class StructuredControlFlowValidatorDraft : IStructuredControlFlowValidatorDraft
{
    public void Validate(StructuredMethodDraft method)
    {
        ArgumentNullException.ThrowIfNull(method);

        var occurrences = new Dictionary<int, int>();
        var owners = new Dictionary<int, int>();
        var visitedGroups = new HashSet<StructuredExceptionGroupDraft>(ReferenceEqualityComparer.Instance);
        Visit(method.Body, occurrences, owners, visitedGroups);

        var reachable = method.ValidatedGraph.Graph.ReachableBlocks;
        var missing = reachable.Where(block => !occurrences.ContainsKey(block)).Order().ToArray();
        var foreign = occurrences.Keys.Where(block => !reachable.Contains(block)).Order().ToArray();
        var invalidOwners = reachable
            .Where(block => owners.GetValueOrDefault(block) != 1)
            .Order()
            .ToArray();

        if (missing.Length != 0 || foreign.Length != 0 || invalidOwners.Length != 0)
        {
            throw new InvalidOperationException(
                $"Invalid structured control flow: missing [{string.Join(", ", missing)}], " +
                $"foreign [{string.Join(", ", foreign)}], ownership [{string.Join(", ", invalidOwners)}].");
        }
    }

    private static void Visit(
        StructuredSequenceDraft sequence,
        Dictionary<int, int> occurrences,
        Dictionary<int, int> owners,
        HashSet<StructuredExceptionGroupDraft> visitedGroups)
    {
        foreach (var region in sequence.Regions)
        {
            switch (region)
            {
                case StructuredBlockDraft block:
                    Record(block.Block.Index, block.IsOriginal, occurrences, owners);
                    break;
                case StructuredIfDraft conditional:
                    Record(conditional.ConditionBlock.Index, conditional.IsOriginal, occurrences, owners);
                    Visit(conditional.WhenTrue, occurrences, owners, visitedGroups);
                    Visit(conditional.WhenFalse, occurrences, owners, visitedGroups);
                    break;
                case StructuredLoopDraft loop:
                    Record(loop.ConditionBlock.Index, loop.IsOriginal, occurrences, owners);
                    Visit(loop.Body, occurrences, owners, visitedGroups);
                    Visit(loop.ContinueBody, occurrences, owners, visitedGroups);
                    Visit(loop.ExitBody, occurrences, owners, visitedGroups);
                    break;
                case StructuredPostTestLoopDraft loop:
                    Visit(loop.Body, occurrences, owners, visitedGroups);
                    Record(loop.ConditionBlock.Index, loop.IsOriginal, occurrences, owners);
                    Visit(loop.ContinueBody, occurrences, owners, visitedGroups);
                    Visit(loop.ExitBody, occurrences, owners, visitedGroups);
                    break;
                case StructuredDispatcherDraft dispatcher:
                    foreach (var block in dispatcher.Blocks)
                    {
                        Record(block.Block.Index, block.IsOriginal, occurrences, owners);
                    }
                    foreach (var exit in dispatcher.Exits)
                    {
                        Visit(exit.Body, occurrences, owners, visitedGroups);
                    }
                    break;
                case StructuredExceptionRegionDraft exception:
                    Visit(exception.Group, occurrences, owners, visitedGroups);
                    break;
            }
        }
    }

    private static void Visit(
        StructuredExceptionGroupDraft group,
        Dictionary<int, int> occurrences,
        Dictionary<int, int> owners,
        HashSet<StructuredExceptionGroupDraft> visitedGroups)
    {
        if (!visitedGroups.Add(group))
        {
            return;
        }

        foreach (var part in group.ProtectedParts)
        {
            Visit(part, occurrences, owners, visitedGroups);
        }
        foreach (var clause in group.Clauses)
        {
            if (clause.FilterBody is not null)
            {
                Visit(clause.FilterBody, occurrences, owners, visitedGroups);
            }
            Visit(clause.HandlerBody, occurrences, owners, visitedGroups);
        }
        foreach (var continuation in group.NormalContinuations)
        {
            Visit(continuation.Body, occurrences, owners, visitedGroups);
        }
        if (group.ContinuationDispatcher is not null)
        {
            Visit(new StructuredSequenceDraft([group.ContinuationDispatcher]), occurrences, owners, visitedGroups);
        }
    }

    private static void Visit(
        StructuredExceptionPartDraft part,
        Dictionary<int, int> occurrences,
        Dictionary<int, int> owners,
        HashSet<StructuredExceptionGroupDraft> visitedGroups)
    {
        switch (part)
        {
            case StructuredExceptionCodeDraft code:
                Visit(code.Body, occurrences, owners, visitedGroups);
                break;
            case StructuredNestedExceptionGroupDraft nested:
                Visit(nested.Group, occurrences, owners, visitedGroups);
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported structured exception part '{part.GetType().Name}'.");
        }
    }

    private static void Record(
        int block,
        bool ownsBody,
        Dictionary<int, int> occurrences,
        Dictionary<int, int> owners)
    {
        occurrences[block] = occurrences.GetValueOrDefault(block) + 1;
        if (ownsBody)
        {
            owners[block] = owners.GetValueOrDefault(block) + 1;
        }
    }
}
