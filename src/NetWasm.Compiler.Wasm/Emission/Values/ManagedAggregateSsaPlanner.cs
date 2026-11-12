using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Values;

internal sealed class ManagedAggregateSsaPlanner : IManagedAggregateSsaPlanner
{
    public ManagedAggregateSsaPlan Plan(ManagedAggregateSsaPlanningRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Method);
        ArgumentNullException.ThrowIfNull(request.Analysis);

        var blocks = ReachableBlocks(request.Method);
        var blockByInstruction = blocks.Values
            .SelectMany(block => block.Instructions.Select(instruction =>
                (instruction.Offset, block.Id)))
            .ToImmutableDictionary(pair => pair.Offset, pair => pair.Id);
        var storages = SelectStorages(request.Analysis, blockByInstruction, request.References);
        if (storages.IsEmpty)
        {
            return ManagedAggregateSsaPlan.Empty;
        }

        var lanes = storages.Values
            .OrderBy(storage => storage.Id.Kind)
            .ThenBy(storage => storage.Id.Index)
            .SelectMany(storage => storage.Lanes.Select(lane => new ManagedAggregateSsaLane(
                storage.Id,
                lane.ByteOffset)))
            .ToImmutableArray();
        var laneSet = lanes.ToImmutableHashSet();
        var accesses = request.Analysis.LaneAccesses.Values
            .Where(access => storages.ContainsKey(access.Address.Storage))
            .ToImmutableDictionary(access => access.InstructionOffset);
        var materializations = request.Analysis.Materializations
            .Where(materialization =>
                storages.ContainsKey(materialization.Storage))
            .ToImmutableArray();
        var successors = blocks.ToImmutableDictionary(
            pair => pair.Key,
            pair => Successors(pair.Value.Exit)
                .Where(blocks.ContainsKey)
                .Distinct()
                .ToImmutableArray());
        var predecessors = blocks.Keys.ToDictionary(
            block => block,
            _ => ImmutableArray.CreateBuilder<StructuredBlockId>());
        foreach ((var block, var targets) in successors)
        {
            foreach (var target in targets)
            {
                predecessors[target].Add(block);
            }
        }
        if (predecessors[request.Method.EntryBlock].Count != 0)
        {
            return ManagedAggregateSsaPlan.Empty;
        }

        var uses = blocks.Keys.ToDictionary(
            block => block,
            _ => ImmutableHashSet.CreateBuilder<ManagedAggregateSsaLane>());
        var definitions = blocks.Keys.ToDictionary(
            block => block,
            _ => ImmutableHashSet.CreateBuilder<ManagedAggregateSsaLane>());
        var materializationsByOffset = materializations
            .GroupBy(materialization => materialization.InstructionOffset)
            .ToDictionary(group => group.Key, group => group.ToImmutableArray());
        var reloadLanes = materializations
            .Where(materialization => materialization.ReloadAfter)
            .GroupBy(materialization => materialization.InstructionOffset)
            .ToImmutableDictionary(group => group.Key, group =>
                group.SelectMany(materialization =>
                        LanesFor(materialization.Storage, laneSet))
                    .Distinct().OrderBy(LaneOrder).ToImmutableArray());
        foreach (var block in blocks.Values)
        {
            foreach (var instruction in block.Instructions)
            {
                if (materializationsByOffset.TryGetValue(
                        instruction.Offset,
                        out var atInstruction))
                {
                    foreach (var materialization in atInstruction)
                    {
                        foreach (var lane in LanesFor(
                                     materialization.Storage,
                                     laneSet))
                        {
                            if (!definitions[block.Id].Contains(lane))
                            {
                                uses[block.Id].Add(lane);
                            }
                            if (materialization.ReloadAfter)
                            {
                                definitions[block.Id].Add(lane);
                            }
                        }
                    }
                }
                if (!accesses.TryGetValue(instruction.Offset, out var access))
                {
                    continue;
                }
                foreach (var lane in AccessLanes(access))
                {
                    if (access.Writes)
                    {
                        definitions[block.Id].Add(lane);
                    }
                    else if (!definitions[block.Id].Contains(lane))
                    {
                        uses[block.Id].Add(lane);
                    }
                }
            }
        }

        var liveIn = blocks.Keys.ToDictionary(
            block => block,
            _ => ImmutableHashSet<ManagedAggregateSsaLane>.Empty);
        var liveOut = blocks.Keys.ToDictionary(
            block => block,
            _ => ImmutableHashSet<ManagedAggregateSsaLane>.Empty);
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var block in blocks.Keys.OrderByDescending(block => block.Value))
            {
                var outgoing = successors[block]
                    .SelectMany(successor => liveIn[successor])
                    .ToImmutableHashSet();
                var incoming = uses[block]
                    .Union(outgoing.Except(definitions[block]))
                    .ToImmutableHashSet();
                if (!outgoing.SetEquals(liveOut[block]) ||
                    !incoming.SetEquals(liveIn[block]))
                {
                    liveOut[block] = outgoing;
                    liveIn[block] = incoming;
                    changed = true;
                }
            }
        }

        var nextValue = 0;
        var initialValues = lanes.ToImmutableDictionary(
            lane => lane,
            _ => new ManagedAggregateSsaValue(nextValue++));
        var definitionList = initialValues
            .Select(pair => new ManagedAggregateSsaDefinition(
                pair.Value,
                pair.Key,
                ManagedAggregateSsaDefinitionKind.Initial,
                request.Method.EntryBlock,
                null))
            .ToList();
        var phiValues = new Dictionary<
            (StructuredBlockId Block, ManagedAggregateSsaLane Lane),
            ManagedAggregateSsaValue>();
        foreach (var block in blocks.Keys.OrderBy(block => block.Value))
        {
            if (predecessors[block].Count < 2)
            {
                continue;
            }
            foreach (var lane in liveIn[block].OrderBy(LaneOrder))
            {
                var value = new ManagedAggregateSsaValue(nextValue++);
                phiValues.Add((block, lane), value);
                definitionList.Add(new(
                    value,
                    lane,
                    ManagedAggregateSsaDefinitionKind.Phi,
                    block,
                    null));
            }
        }

        var storeValues = new Dictionary<
            (int InstructionOffset, ManagedAggregateSsaLane Lane),
            ManagedAggregateSsaValue>();
        foreach (var access in accesses.Values
                     .Where(access => access.Writes)
                     .OrderBy(access => access.InstructionOffset))
        {
            foreach (var lane in AccessLanes(access))
            {
                var value = new ManagedAggregateSsaValue(nextValue++);
                storeValues.Add((access.InstructionOffset, lane), value);
                definitionList.Add(new(
                    value,
                    lane,
                    ManagedAggregateSsaDefinitionKind.Store,
                    access.Block,
                    access.InstructionOffset));
            }
        }

        var entryStates = new Dictionary<
            StructuredBlockId,
            ImmutableDictionary<ManagedAggregateSsaLane, ManagedAggregateSsaValue>>();
        foreach (var (offset, reloaded) in reloadLanes.OrderBy(pair => pair.Key))
        {
            foreach (var lane in reloaded)
            {
                var value = new ManagedAggregateSsaValue(nextValue++);
                storeValues.Add((offset, lane), value);
                definitionList.Add(new(value, lane,
                    ManagedAggregateSsaDefinitionKind.Reload,
                    blockByInstruction[offset], offset));
            }
        }
        var exitStates = new Dictionary<
            StructuredBlockId,
            ImmutableDictionary<ManagedAggregateSsaLane, ManagedAggregateSsaValue>>();
        changed = true;
        while (changed)
        {
            changed = false;
            foreach (var block in blocks.Keys.OrderBy(block => block.Value))
            {
                var entry = EntryState(
                    block,
                    request.Method.EntryBlock,
                    lanes,
                    initialValues,
                    phiValues,
                    predecessors,
                    exitStates);
                var exit = ApplyDefinitions(
                    entry,
                    blocks[block],
                    accesses,
                    storeValues,
                    reloadLanes);
                if (!entryStates.TryGetValue(block, out var priorEntry) ||
                    !StateEquals(priorEntry, entry) ||
                    !exitStates.TryGetValue(block, out var priorExit) ||
                    !StateEquals(priorExit, exit))
                {
                    entryStates[block] = entry;
                    exitStates[block] = exit;
                    changed = true;
                }
            }
        }

        var accessPlans = ImmutableDictionary.CreateBuilder<int, ManagedAggregateSsaAccess>();
        var materializationPlans = ImmutableArray.CreateBuilder<
            ManagedAggregateSsaMaterialization>();
        var materializationBlocks = new List<StructuredBlockId>();
        foreach (var block in blocks.Values.OrderBy(block => block.Id.Value))
        {
            var state = entryStates[block.Id].ToBuilder();
            foreach (var instruction in block.Instructions)
            {
                if (materializationsByOffset.TryGetValue(
                        instruction.Offset,
                        out var atInstruction))
                {
                    foreach (var materialization in atInstruction)
                    {
                        var incoming = LanesFor(materialization.Storage, laneSet)
                            .OrderBy(LaneOrder)
                            .Select(lane => new ManagedAggregateSsaLaneValue(lane, state[lane]))
                            .ToImmutableArray();
                        var reloaded = materialization.ReloadAfter
                            ? incoming.Select(value => new ManagedAggregateSsaLaneValue(
                                value.Lane, storeValues[(instruction.Offset, value.Lane)]))
                                .ToImmutableArray()
                            : ImmutableArray<ManagedAggregateSsaLaneValue>.Empty;
                        materializationPlans.Add(new(
                            materialization,
                            incoming)
                        { Reloads = reloaded });
                        materializationBlocks.Add(block.Id);
                        foreach (var value in reloaded)
                        {
                            state[value.Lane] = value.Value;
                        }
                    }
                }
                if (!accesses.TryGetValue(instruction.Offset, out var access))
                {
                    continue;
                }
                var affected = AccessLanes(access).OrderBy(LaneOrder).ToImmutableArray();
                var reads = access.Writes
                    ? ImmutableArray<ManagedAggregateSsaLaneValue>.Empty
                    : [.. affected.Select(lane =>
                        new ManagedAggregateSsaLaneValue(lane, state[lane]))];
                var writes = access.Writes
                    ? [.. affected.Select(lane =>
                    {
                        var value = storeValues[(access.InstructionOffset, lane)];
                        state[lane] = value;
                        return new ManagedAggregateSsaLaneValue(lane, value);
                    })]
                    : ImmutableArray<ManagedAggregateSsaLaneValue>.Empty;
                accessPlans.Add(
                    instruction.Offset,
                    new(access, reads, writes));
            }
        }

        var phiDrafts = phiValues
            .OrderBy(pair => pair.Key.Block.Value)
            .ThenBy(pair => LaneOrder(pair.Key.Lane))
            .Select(pair => new ManagedAggregateSsaPhi(
                pair.Key.Block,
                pair.Key.Lane,
                pair.Value,
                predecessors[pair.Key.Block]
                    .OrderBy(block => block.Value)
                    .ToImmutableDictionary(
                        predecessor => predecessor,
                        predecessor => exitStates[predecessor][pair.Key.Lane])))
            .ToImmutableArray();
        var replacements = CollapseTrivialPhis(phiDrafts);
        var phis = phiDrafts
            .Where(phi => !replacements.ContainsKey(phi.Result))
            .Select(phi => phi with
            {
                Incoming = phi.Incoming.ToImmutableDictionary(
                    pair => pair.Key,
                    pair => Resolve(pair.Value, replacements)),
            })
            .ToImmutableArray();
        var edgeCopies = phis
            .SelectMany(phi => phi.Incoming.Select(incoming =>
                new ManagedAggregateSsaEdgeCopy(
                    incoming.Key,
                    phi.Block,
                    phi.Lane,
                    Resolve(incoming.Value, replacements),
                    phi.Result)))
            .Where(copy => copy.Source != copy.Destination)
            .OrderBy(copy => copy.From.Value)
            .ThenBy(copy => copy.To.Value)
            .ThenBy(copy => LaneOrder(copy.Lane))
            .ToImmutableArray();
        return new(
            storages,
            lanes,
            initialValues,
            [.. definitionList
                .Where(definition => !replacements.ContainsKey(definition.Value))
                .OrderBy(definition => definition.Value.Id)],
            accessPlans.ToImmutableDictionary(
                pair => pair.Key,
                pair => pair.Value with
                {
                    Reads = Canonicalize(pair.Value.Reads, replacements),
                    Writes = Canonicalize(pair.Value.Writes, replacements),
                }),
            phis,
            edgeCopies,
            ElideHeldStores(
                [.. materializationPlans.Select(materialization => materialization with
                {
                    Values = Canonicalize(materialization.Values, replacements),
                    Reloads = Canonicalize(materialization.Reloads, replacements),
                })],
                [.. materializationBlocks],
                blocks,
                request.Method.EntryBlock,
                predecessors,
                successors,
                phis),
            [.. request.Analysis.RootDependencies.Where(dependency =>
                storages.ContainsKey(dependency.Address.Storage))]);
    }

    // A materialization stores every lane before a call takes the storage's address, but where the
    // frame already holds a lane's value (stored or reloaded there since, on every path, and not
    // written in a lane since) the store is left out, so the frame holds what it held. A loop's phi
    // is held where each incoming edge holds its input.
    // Whether two exits hold the same value in every lane, so a block's facts stopped changing.
    internal static bool HoldsTheSame(
        ImmutableDictionary<ManagedAggregateSsaLane, ManagedAggregateSsaValue> prior,
        ImmutableDictionary<ManagedAggregateSsaLane, ManagedAggregateSsaValue> exit) =>
        prior.Count == exit.Count &&
        prior.All(pair => exit.TryGetValue(pair.Key, out var value) && value == pair.Value);

    private static ImmutableArray<ManagedAggregateSsaMaterialization> ElideHeldStores(
        ImmutableArray<ManagedAggregateSsaMaterialization> materializations,
        ImmutableArray<StructuredBlockId> materializationBlocks,
        ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition> blocks,
        StructuredBlockId entryBlock,
        Dictionary<StructuredBlockId, ImmutableArray<StructuredBlockId>.Builder> predecessors,
        ImmutableDictionary<StructuredBlockId, ImmutableArray<StructuredBlockId>> successors,
        ImmutableArray<ManagedAggregateSsaPhi> phis)
    {
        var inBlock = materializationBlocks
            .Select((block, index) => (Block: block, Index: index))
            .ToLookup(pair => pair.Block, pair => pair.Index);
        var phiByLane = phis.ToDictionary(phi => (phi.Block, phi.Lane));
        // A block's exit is known once some path reaches it; the entry block starts with nothing held.
        var exits = new Dictionary<StructuredBlockId, ImmutableDictionary<ManagedAggregateSsaLane, ManagedAggregateSsaValue>>();
        var pending = new Queue<StructuredBlockId>([entryBlock]);
        while (pending.TryDequeue(out var block))
        {
            var held = Held(block).ToBuilder();
            foreach (var index in inBlock[block]) Hold(held, materializations[index]);
            var exit = held.ToImmutable();
            if (exits.TryGetValue(block, out var prior) && HoldsTheSame(prior, exit))
            {
                continue;
            }
            exits[block] = exit;
            foreach (var successor in successors[block]) pending.Enqueue(successor);
        }

        var result = materializations.ToBuilder();
        foreach (var block in blocks.Keys.Where(exits.ContainsKey))
        {
            var held = Held(block).ToBuilder();
            foreach (var index in inBlock[block])
            {
                var materialization = materializations[index];
                result[index] = materialization with
                {
                    Values = [.. materialization.Values.Where(value =>
                        !(held.TryGetValue(value.Lane, out var memory) && memory == value.Value))],
                };
                Hold(held, materialization);
            }
        }
        return result.ToImmutable();

        ImmutableDictionary<ManagedAggregateSsaLane, ManagedAggregateSsaValue> Held(StructuredBlockId block)
        {
            // A block other than the entry is reached through a predecessor that was.
            if (block == entryBlock) return [];
            var reached = predecessors[block].Where(exits.ContainsKey).ToArray();
            var held = ImmutableDictionary.CreateBuilder<ManagedAggregateSsaLane, ManagedAggregateSsaValue>();
            foreach (var (lane, value) in exits[reached[0]])
            {
                if (phiByLane.TryGetValue((block, lane), out var phi))
                {
                    if (reached.All(predecessor => exits[predecessor].TryGetValue(lane, out var memory) &&
                            memory == phi.Incoming[predecessor]))
                    {
                        held.Add(lane, phi.Result);
                    }
                }
                else if (reached.All(predecessor =>
                             exits[predecessor].TryGetValue(lane, out var memory) && memory == value))
                {
                    held.Add(lane, value);
                }
            }
            return held.ToImmutable();
        }

        static void Hold(
            ImmutableDictionary<ManagedAggregateSsaLane, ManagedAggregateSsaValue>.Builder held,
            ManagedAggregateSsaMaterialization materialization)
        {
            foreach (var value in materialization.Values) held[value.Lane] = value.Value;
            foreach (var value in materialization.Reloads) held[value.Lane] = value.Value;
        }
    }

    private static Dictionary<ManagedAggregateSsaValue, ManagedAggregateSsaValue>
        CollapseTrivialPhis(ImmutableArray<ManagedAggregateSsaPhi> phis)
    {
        var replacements = new Dictionary<
            ManagedAggregateSsaValue,
            ManagedAggregateSsaValue>();
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var phi in phis)
            {
                if (replacements.ContainsKey(phi.Result))
                {
                    continue;
                }
                var incoming = phi.Incoming.Values
                    .Select(value => Resolve(value, replacements))
                    .Where(value => value != phi.Result)
                    .Distinct()
                    .Take(2)
                    .ToArray();
                if (incoming.Length == 1)
                {
                    replacements.Add(phi.Result, incoming[0]);
                    changed = true;
                }
            }
        }
        return replacements;
    }

    private static ManagedAggregateSsaValue Resolve(
        ManagedAggregateSsaValue value,
        IReadOnlyDictionary<ManagedAggregateSsaValue, ManagedAggregateSsaValue>
            replacements)
    {
        while (replacements.TryGetValue(value, out var replacement))
        {
            value = replacement;
        }
        return value;
    }

    private static ImmutableArray<ManagedAggregateSsaLaneValue> Canonicalize(
        ImmutableArray<ManagedAggregateSsaLaneValue> values,
        IReadOnlyDictionary<ManagedAggregateSsaValue, ManagedAggregateSsaValue>
            replacements) =>
        [.. values.Select(value => value with
        {
            Value = Resolve(value.Value, replacements),
        })];

    private static ImmutableDictionary<ManagedAggregateStorageId, ManagedAggregateStorage>
        SelectStorages(
            ManagedAggregateValueAnalysis analysis,
            ImmutableDictionary<int, StructuredBlockId> blockByInstruction,
            bool requireRead)
    {
        var selected = ImmutableDictionary.CreateBuilder<
            ManagedAggregateStorageId,
            ManagedAggregateStorage>();
        foreach ((var id, var storage) in analysis.Storages)
        {
            var accesses = analysis.LaneAccesses.Values
                .Where(access => access.Address.Storage == id)
                .ToArray();
            // Lanes pay off only when a read takes its value from one; a
            // storage that is only written still stores every lane at its
            // materializations. The References level, which also promotes
            // initobj, requires a read.
            if ((!accesses.Any(access => access.Writes) &&
                 !analysis.Materializations.Any(materialization =>
                     materialization.Storage == id && materialization.ReloadAfter)) ||
                requireRead && accesses.All(access => access.Writes) ||
                accesses.Any(access => !IsSupported(access)) ||
                analysis.Materializations.Any(materialization =>
                    materialization.Storage == id &&
                    !blockByInstruction.ContainsKey(materialization.InstructionOffset)))
            {
                continue;
            }
            selected.Add(id, storage);
        }
        return selected.ToImmutable();
    }

    // Integer accesses span 4-byte lanes; a reference or byref access is its
    // own lane, and initobj clears every lane.
    private static bool IsSupported(ManagedAggregateLaneAccess access) =>
        access.LaneOffsets.IsDefaultOrEmpty
            ? access.Address.ByteOffset % sizeof(int) == 0 &&
                access.Size is sizeof(int) or sizeof(long) &&
                access.StackKind is CliValueKind.I4 or CliValueKind.I8
            : access.StackKind is CliValueKind.ManagedAddress or CliValueKind.ManagedReference or CliValueKind.ValueType;

    private static IEnumerable<ManagedAggregateSsaLane> AccessLanes(
        ManagedAggregateLaneAccess access) =>
        access.LaneOffsets.IsDefaultOrEmpty
            ? Enumerable.Range(0, access.Size / sizeof(int))
                .Select(index => new ManagedAggregateSsaLane(
                    access.Address.Storage,
                    access.Address.ByteOffset + index * sizeof(int)))
            : access.LaneOffsets.Select(offset => new ManagedAggregateSsaLane(access.Address.Storage, offset));

    private static IEnumerable<ManagedAggregateSsaLane> LanesFor(
        ManagedAggregateStorageId storage,
        ImmutableHashSet<ManagedAggregateSsaLane> lanes) =>
        lanes.Where(lane => lane.Storage == storage);

    private static ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition>
        ReachableBlocks(StructuredMethod method)
    {
        var reachable = ImmutableDictionary.CreateBuilder<
            StructuredBlockId,
            StructuredBlockDefinition>();
        var pending = new Queue<StructuredBlockId>();
        pending.Enqueue(method.EntryBlock);
        while (pending.TryDequeue(out var id))
        {
            if (!method.Blocks.TryGetValue(id, out var block) ||
                reachable.ContainsKey(id))
            {
                continue;
            }
            reachable.Add(id, block);
            foreach (var successor in Successors(block.Exit))
            {
                pending.Enqueue(successor);
            }
        }
        return reachable.ToImmutable();
    }

    private static ImmutableDictionary<ManagedAggregateSsaLane, ManagedAggregateSsaValue>
        EntryState(
            StructuredBlockId block,
            StructuredBlockId entryBlock,
            ImmutableArray<ManagedAggregateSsaLane> lanes,
            ImmutableDictionary<ManagedAggregateSsaLane, ManagedAggregateSsaValue>
                initialValues,
            Dictionary<
                (StructuredBlockId Block, ManagedAggregateSsaLane Lane),
                ManagedAggregateSsaValue> phiValues,
            Dictionary<StructuredBlockId, ImmutableArray<StructuredBlockId>.Builder>
                predecessors,
            Dictionary<
                StructuredBlockId,
                ImmutableDictionary<ManagedAggregateSsaLane, ManagedAggregateSsaValue>>
                exitStates)
    {
        var state = ImmutableDictionary.CreateBuilder<
            ManagedAggregateSsaLane,
            ManagedAggregateSsaValue>();
        foreach (var lane in lanes)
        {
            if (phiValues.TryGetValue((block, lane), out var phi))
            {
                state.Add(lane, phi);
                continue;
            }
            if (block == entryBlock || predecessors[block].Count == 0)
            {
                state.Add(lane, initialValues[lane]);
                continue;
            }
            var predecessor = predecessors[block][0];
            state.Add(
                lane,
                exitStates.TryGetValue(predecessor, out var exit)
                    ? exit[lane]
                    : initialValues[lane]);
        }
        return state.ToImmutable();
    }

    private static ImmutableDictionary<ManagedAggregateSsaLane, ManagedAggregateSsaValue>
        ApplyDefinitions(
            ImmutableDictionary<ManagedAggregateSsaLane, ManagedAggregateSsaValue> entry,
            StructuredBlockDefinition block,
            ImmutableDictionary<int, ManagedAggregateLaneAccess> accesses,
            Dictionary<
                (int InstructionOffset, ManagedAggregateSsaLane Lane),
            ManagedAggregateSsaValue> storeValues,
        ImmutableDictionary<int, ImmutableArray<ManagedAggregateSsaLane>> reloadLanes)
    {
        var state = entry.ToBuilder();
        foreach (var instruction in block.Instructions)
        {
            if (reloadLanes.TryGetValue(instruction.Offset, out var reloaded))
            {
                foreach (var lane in reloaded)
                {
                    state[lane] = storeValues[(instruction.Offset, lane)];
                }
            }
            if (!accesses.TryGetValue(instruction.Offset, out var access) ||
                !access.Writes)
            {
                continue;
            }
            foreach (var lane in AccessLanes(access))
            {
                state[lane] = storeValues[(instruction.Offset, lane)];
            }
        }
        return state.ToImmutable();
    }

    private static bool StateEquals(
        ImmutableDictionary<ManagedAggregateSsaLane, ManagedAggregateSsaValue> left,
        ImmutableDictionary<ManagedAggregateSsaLane, ManagedAggregateSsaValue> right) =>
        // Every state contains the same selected lanes; definitions only replace values.
        left.All(pair => right[pair.Key] == pair.Value);

    private static int LaneOrder(ManagedAggregateSsaLane lane) => checked(
        ((int)lane.Storage.Kind * 1_000_000) +
        (lane.Storage.Index * 1_000) +
        lane.ByteOffset);

    private static ImmutableArray<StructuredBlockId> Successors(
        StructuredBlockExit exit) => exit switch
        {
            StructuredFallthroughExit { Target: { } target } => [target],
            StructuredBranchExit branch => [branch.Target],
            StructuredConditionalExit conditional =>
                [conditional.WhenTaken, conditional.WhenNotTaken],
            StructuredLeaveExit leave => [leave.Target],
            _ => [],
        };
}
