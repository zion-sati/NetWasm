using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

// A forward must-analysis of root-frame facts over a method's blocks. The fact at a block's entry is
// the meet of the exits of its ordinary predecessors that some path reaches, and the empty fact at
// an opaque block (the method's entry among them). Starting from the opaque blocks, exits only
// shrink, so the iteration stops at the greatest fixpoint; a block no path reaches gets the empty
// fact.
internal static class RootFactFlow
{
    public static ImmutableDictionary<StructuredBlockId, TFact> Solve<TFact>(
        StructuredMethod method,
        StructuredBlockPredecessors flow,
        TFact empty,
        Func<TFact, TFact, TFact> meet,
        Func<TFact, TFact, bool> equal,
        Func<StructuredBlockDefinition, TFact, TFact> transfer)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(flow);
        var exits = new Dictionary<StructuredBlockId, TFact>();
        var successors = flow.Predecessors
            .SelectMany(pair => pair.Value.Select(predecessor => (Predecessor: predecessor, Block: pair.Key)))
            .ToLookup(edge => edge.Predecessor, edge => edge.Block);
        var pending = new Queue<StructuredBlockId>();
        var queued = new HashSet<StructuredBlockId>();
        foreach (var id in method.Blocks.Keys.Where(flow.Opaque.Contains).OrderBy(id => id.Value))
        {
            pending.Enqueue(id);
            queued.Add(id);
        }
        while (pending.TryDequeue(out var id))
        {
            queued.Remove(id);
            var exit = transfer(method.Blocks[id], Entry(id));
            if (exits.TryGetValue(id, out var previous) && equal(previous, exit)) continue;
            exits[id] = exit;
            foreach (var successor in successors[id])
            {
                if (queued.Add(successor)) pending.Enqueue(successor);
            }
        }
        return method.Blocks.Keys.ToImmutableDictionary(id => id, id => exits.ContainsKey(id) ? Entry(id) : empty);

        TFact Entry(StructuredBlockId id)
        {
            if (flow.Opaque.Contains(id)) return empty;
            var reached = flow.Predecessors[id].Where(exits.ContainsKey).ToArray();
            // A block is queued only once a predecessor is reached.
            return reached.Skip(1).Aggregate(exits[reached[0]], (fact, predecessor) => meet(fact, exits[predecessor]));
        }
    }
}
