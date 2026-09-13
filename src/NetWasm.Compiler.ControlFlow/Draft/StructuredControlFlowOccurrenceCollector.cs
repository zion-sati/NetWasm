using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow.Draft;

internal sealed class StructuredControlFlowOccurrenceCollector : IStructuredControlFlowOccurrenceCollector
{
    public ImmutableArray<StructuredControlFlowBlockOccurrence> Collect(StructuredSequenceDraft sequence)
    {
        ArgumentNullException.ThrowIfNull(sequence);

        var occurrences = ImmutableArray.CreateBuilder<StructuredControlFlowBlockOccurrence>();
        Collect(sequence, occurrences, new HashSet<StructuredExceptionGroupDraft>(ReferenceEqualityComparer.Instance));
        return occurrences.ToImmutable();
    }

    private static void Collect(
        StructuredSequenceDraft sequence,
        ImmutableArray<StructuredControlFlowBlockOccurrence>.Builder occurrences,
        HashSet<StructuredExceptionGroupDraft> visitedGroups)
    {
        foreach (var region in sequence.Regions)
        {
            switch (region)
            {
                case StructuredBlockDraft block:
                    Add(block.Block.Index, block.IsOriginal, occurrences);
                    break;
                case StructuredIfDraft conditional:
                    Add(conditional.ConditionBlock.Index, conditional.IsOriginal, occurrences);
                    Collect(conditional.WhenTrue, occurrences, visitedGroups);
                    Collect(conditional.WhenFalse, occurrences, visitedGroups);
                    break;
                case StructuredLoopDraft loop:
                    Add(loop.ConditionBlock.Index, loop.IsOriginal, occurrences);
                    Collect(loop.Body, occurrences, visitedGroups);
                    Collect(loop.ContinueBody, occurrences, visitedGroups);
                    Collect(loop.ExitBody, occurrences, visitedGroups);
                    break;
                case StructuredPostTestLoopDraft loop:
                    Collect(loop.Body, occurrences, visitedGroups);
                    Add(loop.ConditionBlock.Index, loop.IsOriginal, occurrences);
                    Collect(loop.ContinueBody, occurrences, visitedGroups);
                    Collect(loop.ExitBody, occurrences, visitedGroups);
                    break;
                case StructuredDispatcherDraft dispatcher:
                    foreach (var block in dispatcher.Blocks)
                    {
                        Add(block.Block.Index, block.IsOriginal, occurrences);
                    }
                    foreach (var exit in dispatcher.Exits)
                    {
                        Collect(exit.Body, occurrences, visitedGroups);
                    }
                    break;
                case StructuredExceptionRegionDraft exception:
                    Collect(exception.Group, occurrences, visitedGroups);
                    break;
            }
        }
    }

    private static void Collect(
        StructuredExceptionGroupDraft group,
        ImmutableArray<StructuredControlFlowBlockOccurrence>.Builder occurrences,
        HashSet<StructuredExceptionGroupDraft> visitedGroups)
    {
        if (!visitedGroups.Add(group))
        {
            return;
        }

        foreach (var part in group.ProtectedParts)
        {
            switch (part)
            {
                case StructuredExceptionCodeDraft code:
                    Collect(code.Body, occurrences, visitedGroups);
                    break;
                case StructuredNestedExceptionGroupDraft nested:
                    Collect(nested.Group, occurrences, visitedGroups);
                    break;
                default:
                    throw new InvalidOperationException("Unsupported structured exception part.");
            }
        }

        foreach (var clause in group.Clauses)
        {
            if (clause.FilterBody is not null)
            {
                Collect(clause.FilterBody, occurrences, visitedGroups);
            }
            Collect(clause.HandlerBody, occurrences, visitedGroups);
        }

        foreach (var continuation in group.NormalContinuations)
        {
            Collect(continuation.Body, occurrences, visitedGroups);
        }

        if (group.ContinuationDispatcher is not null)
        {
            Collect(new StructuredSequenceDraft([group.ContinuationDispatcher]), occurrences, visitedGroups);
        }
    }

    private static void Add(
        int block,
        bool preferred,
        ImmutableArray<StructuredControlFlowBlockOccurrence>.Builder occurrences) =>
        occurrences.Add(new StructuredControlFlowBlockOccurrence(block, occurrences.Count, preferred));
}
