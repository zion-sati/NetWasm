using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Values.AggregateAnalysis;

namespace NetWasm.Compiler.Wasm.Emission.Values;

// Finds the address-taken aggregates whose values can live in lanes: a forward dataflow over the method's blocks
// tracks where each managed address may point, the block transfer records what each instruction does to the candidate
// storages, and whatever escapes, or is stored more often than its lanes are read, stays in memory.
internal sealed class ManagedAggregateValueAnalyzer(
    IManagedAggregateStorageFinder storageFinder,
    IManagedAggregateBlockTransfer blocks,
    IUnamortizedAggregateStoreRejector unamortizedStores) : IManagedAggregateValueAnalyzer
{
    private readonly IManagedAggregateStorageFinder _storageFinder = storageFinder ??
        throw new ArgumentNullException(nameof(storageFinder));
    private readonly IManagedAggregateBlockTransfer _blocks = blocks ??
        throw new ArgumentNullException(nameof(blocks));
    private readonly IUnamortizedAggregateStoreRejector _unamortizedStores = unamortizedStores ??
        throw new ArgumentNullException(nameof(unamortizedStores));

    public ManagedAggregateValueAnalysis Analyze(
        ManagedAggregateValueAnalysisRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Method);
        ArgumentNullException.ThrowIfNull(request.RootMap);
        ArgumentNullException.ThrowIfNull(request.ByRefEffects);
        if (request.RootMap.Method != request.Method.Header.Method.Key)
        {
            throw new InvalidOperationException(
                "Aggregate value analysis received a root map for another method.");
        }
        if (!request.Enabled ||
            !request.Method.ExceptionGroups.IsEmpty ||
            !request.RootMap.AnchorPlan.Anchors.IsEmpty)
        {
            return ManagedAggregateValueAnalysis.Empty;
        }

        var storages = _storageFinder.Find(request);
        if (storages.Count == 0)
        {
            return ManagedAggregateValueAnalysis.Empty;
        }

        var session = new ManagedAggregateAnalysisSession(request, storages);
        var entries = new Dictionary<StructuredBlockId, ManagedAggregateFlowState>();
        var pending = new Queue<StructuredBlockId>();
        Merge(request.Method.EntryBlock, new(
            [],
            InitialLocals(request.Method.Header.LocalSignatureTypes)));

        while (pending.TryDequeue(out var blockId))
        {
            var exit = _blocks.Apply(blockId, entries[blockId], session);
            foreach (var successor in ManagedAggregateBlockExits.Successors(request.Method.Blocks[blockId].Exit))
            {
                if (request.Method.Blocks.ContainsKey(successor))
                {
                    Merge(successor, exit);
                }
            }
        }

        _unamortizedStores.Reject(session);
        var selected = storages
            .Where(pair => !session.Rejected.Contains(pair.Key))
            .ToImmutableDictionary();
        if (selected.IsEmpty)
        {
            return ManagedAggregateValueAnalysis.Empty;
        }

        var selectedAccesses = session.Accesses
            .Where(pair => selected.ContainsKey(pair.Value.Address.Storage))
            .ToImmutableDictionary();
        var liveIn = entries.ToImmutableDictionary(
            pair => pair.Key,
            pair => Storages(pair.Value)
                .Where(selected.ContainsKey)
                .ToImmutableHashSet());
        return new(
            selected,
            selectedAccesses,
            liveIn,
            [.. session.RootDependencies.Where(dependency =>
                    selected.ContainsKey(dependency.Address.Storage))
                .Distinct()],
            [.. session.Materializations
                .Where(materialization =>
                    selected.ContainsKey(materialization.Storage))
                .GroupBy(materialization =>
                    (materialization.InstructionOffset, materialization.Storage))
                .Select(group => new ManagedAggregateMaterialization(
                    group.Key.InstructionOffset,
                    group.Key.Storage,
                    group.Any(materialization => materialization.ReloadAfter)))]);

        void Merge(StructuredBlockId block, ManagedAggregateFlowState incoming)
        {
            if (!entries.TryGetValue(block, out var existing))
            {
                entries.Add(block, incoming);
                pending.Enqueue(block);
                return;
            }

            var merged = new ManagedAggregateFlowState(
                Union(existing.Stack, incoming.Stack),
                Union(existing.Locals, incoming.Locals));
            if (merged.Stack.AsSpan().SequenceEqual(existing.Stack.AsSpan()) &&
                merged.Locals.AsSpan().SequenceEqual(existing.Locals.AsSpan()))
            {
                return;
            }
            entries[block] = merged;
            pending.Enqueue(block);
        }
    }

    private static ImmutableArray<ManagedAggregateAddressOrigins> InitialLocals(
        ImmutableArray<CliTypeIdentity> types) =>
        [.. types.Select(type => type.StackKind == CliValueKind.ManagedAddress
            ? ManagedAggregateAddressOrigins.UnknownOnly
            : ManagedAggregateAddressOrigins.None)];

    private static IEnumerable<ManagedAggregateStorageId> Storages(ManagedAggregateFlowState state) =>
        state.Stack.Concat(state.Locals)
            .SelectMany(origins => origins.Recipes)
            .Select(recipe => recipe.Storage)
            .Distinct();

    private static ImmutableArray<ManagedAggregateAddressOrigins> Union(
        ImmutableArray<ManagedAggregateAddressOrigins> left,
        ImmutableArray<ManagedAggregateAddressOrigins> right) =>
        [.. left.Select((origins, index) => origins.Union(right[index]))];
}
