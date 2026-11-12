using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

// The ordinary control-flow edges into each block, for facts that hold on every path into it. A
// block is opaque when control can reach it some other way: an exception handler or filter entry,
// a leave target (a finally may run on the way), an exception group's continuation join, or a
// block no ordinary edge reaches. Nothing is known at an opaque block's entry, nor at the method's.
internal sealed record StructuredBlockPredecessors(
    ImmutableDictionary<StructuredBlockId, ImmutableArray<StructuredBlockId>> Predecessors,
    ImmutableHashSet<StructuredBlockId> Opaque);

internal interface IStructuredBlockPredecessorReader
{
    StructuredBlockPredecessors Read(StructuredMethod method);
}

internal sealed class StructuredBlockPredecessorReader : IStructuredBlockPredecessorReader
{
    public StructuredBlockPredecessors Read(StructuredMethod method)
    {
        ArgumentNullException.ThrowIfNull(method);
        var predecessors = method.Blocks.Keys.ToDictionary(block => block, _ => new List<StructuredBlockId>());
        var opaque = new HashSet<StructuredBlockId> { method.EntryBlock };
        foreach (var (id, block) in method.Blocks)
        {
            switch (block.Exit)
            {
                case StructuredFallthroughExit { Target: { } target }:
                    Add(target, id);
                    break;
                case StructuredBranchExit branch:
                    Add(branch.Target, id);
                    break;
                case StructuredConditionalExit conditional:
                    Add(conditional.WhenTaken, id);
                    Add(conditional.WhenNotTaken, id);
                    break;
                case StructuredLeaveExit leave:
                    opaque.Add(leave.Target);
                    break;
            }
        }
        foreach (var group in method.ExceptionGroups.Values)
        {
            foreach (var clause in group.Clauses)
            {
                opaque.Add(clause.HandlerBlock);
                if (clause.FilterBlock is { } filter) opaque.Add(filter);
            }
            foreach (var continuation in group.NormalContinuations) opaque.Add(continuation.Target);
            if (group.ContinuationJoinBlock is { } join) opaque.Add(join);
        }
        foreach (var (id, incoming) in predecessors)
        {
            if (incoming.Count == 0) opaque.Add(id);
        }
        return new(
            predecessors.ToImmutableDictionary(pair => pair.Key, pair => pair.Value.Distinct().ToImmutableArray()),
            opaque.ToImmutableHashSet());

        void Add(StructuredBlockId target, StructuredBlockId source)
        {
            // An edge to a block outside the method's definitions is not followed.
            if (predecessors.TryGetValue(target, out var incoming)) incoming.Add(source);
            else opaque.Add(target);
        }
    }
}
