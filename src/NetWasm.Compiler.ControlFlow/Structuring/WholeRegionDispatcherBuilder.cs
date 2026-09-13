using NetWasm.Compiler.ControlFlow.Draft;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal sealed class WholeRegionDispatcherBuilder(ICilConditionalBranchClassifier conditionalBranches) : IWholeRegionDispatcherBuilder
{
    private readonly ICilConditionalBranchClassifier _conditionalBranches = conditionalBranches ?? throw new ArgumentNullException(nameof(conditionalBranches));
    public StructuredDispatcherDraft Build(ControlFlowStructuringState state,
        int? entry,
        ImmutableHashSet<int> component)
    {
        var blocks = component.Order().Select(node =>
        {
            var block = state.Graph.GetBlock(node);
            var successors = state.Graph.Successors[node];
            if (_conditionalBranches.Classify(block.Terminator.Operation))
            {
                var branchSelectsFirst =
                    block.Terminator.Operation != CilOperation.BranchIfFalse;
                return new StructuredDispatcherBlockDraft(
                    block,
                    branchSelectsFirst ? successors[0] : successors[1],
                    branchSelectsFirst ? successors[1] : successors[0])
                {
                    IsOriginal = state.ClaimedBlockBodies.Add(block.Index),
                };
            }
            return new StructuredDispatcherBlockDraft(
                block,
                successors.Length == 0 ? null : successors[0],
                null)
            {
                IsOriginal = state.ClaimedBlockBodies.Add(block.Index),
            };
        }).ToImmutableArray();
        var exits = component
            .SelectMany(node => state.Graph.Successors[node])
            .Where(successor => !component.Contains(successor))
            .Distinct()
            .Order()
            .Select(target => new StructuredDispatcherExitDraft(
                target,
                StructuredSequenceDraft.Empty))
            .ToImmutableArray();
        return new StructuredDispatcherDraft(entry, blocks, exits);
    }
}
