using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace NetWasm.Compiler.ControlFlow.Draft;

internal sealed class StructuredControlFlowOwnershipProjector : IStructuredControlFlowOwnershipProjector
{
    public StructuredMethodDraft Project(
        StructuredMethodDraft method,
        ImmutableDictionary<int, int> selectedOwners)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(selectedOwners);
        return new Projection(selectedOwners).Project(method);
    }

    private sealed class Projection(ImmutableDictionary<int, int> selectedOwners)
    {
        private readonly Dictionary<StructuredExceptionGroupDraft, StructuredExceptionGroupDraft> _groups =
            new(ReferenceEqualityComparer.Instance);
        private int _ordinal;

        internal StructuredMethodDraft Project(StructuredMethodDraft method) => method with
        {
            Body = Project(method.Body),
            ExceptionGroups = [.. method.ExceptionGroups.Select(Project)]
        };

        private StructuredSequenceDraft Project(StructuredSequenceDraft sequence) =>
            new([.. sequence.Regions.Select(Project)]);

        private StructuredExceptionGroupDraft Project(StructuredExceptionGroupDraft group)
        {
            if (_groups.TryGetValue(group, out var projected))
            {
                return projected;
            }

            projected = group with
            {
                ProtectedParts = [.. group.ProtectedParts.Select(Project)],
                Clauses = [.. group.Clauses.Select(clause => clause with
                {
                    FilterBody = clause.FilterBody is null ? null : Project(clause.FilterBody),
                    HandlerBody = Project(clause.HandlerBody),
                })],
                NormalContinuations = [.. group.NormalContinuations.Select(continuation => continuation with
                {
                    Body = Project(continuation.Body)
                })],
                ContinuationDispatcher = group.ContinuationDispatcher is null
                    ? null
                    : (StructuredDispatcherDraft)Project(group.ContinuationDispatcher),
            };
            _groups.Add(group, projected);
            return projected;
        }

        private StructuredExceptionPartDraft Project(StructuredExceptionPartDraft part) => part switch
        {
            StructuredExceptionCodeDraft code => code with { Body = Project(code.Body) },
            StructuredNestedExceptionGroupDraft nested => nested with { Group = Project(nested.Group) },
            _ => throw new InvalidOperationException($"Unknown exception part {part.GetType().Name}."),
        };

        private StructuredRegionDraft Project(StructuredRegionDraft region) => region switch
        {
            StructuredBlockDraft block => block with { IsOriginal = Owns(block.Block.Index) },
            StructuredIfDraft conditional => conditional with
            {
                IsOriginal = Owns(conditional.ConditionBlock.Index),
                WhenTrue = Project(conditional.WhenTrue),
                WhenFalse = Project(conditional.WhenFalse),
            },
            StructuredLoopDraft loop => loop with
            {
                IsOriginal = Owns(loop.ConditionBlock.Index),
                Body = Project(loop.Body),
                ContinueBody = Project(loop.ContinueBody),
                ExitBody = Project(loop.ExitBody),
            },
            StructuredPostTestLoopDraft loop => Project(loop),
            StructuredDispatcherDraft dispatcher => dispatcher with
            {
                Blocks = [.. dispatcher.Blocks.Select(block => block with
                {
                    IsOriginal = Owns(block.Block.Index),
                })],
                Exits = [.. dispatcher.Exits.Select(exit => exit with
                {
                    Body = Project(exit.Body),
                })],
            },
            StructuredExceptionRegionDraft exception => exception with
            {
                Group = Project(exception.Group),
            },
            _ => region,
        };

        private StructuredPostTestLoopDraft Project(StructuredPostTestLoopDraft loop)
        {
            var body = Project(loop.Body);
            var ownsCondition = Owns(loop.ConditionBlock.Index);
            return loop with
            {
                Body = body,
                IsOriginal = ownsCondition,
                ContinueBody = Project(loop.ContinueBody),
                ExitBody = Project(loop.ExitBody),
            };
        }

        private bool Owns(int block) => selectedOwners[block] == _ordinal++;
    }
}
