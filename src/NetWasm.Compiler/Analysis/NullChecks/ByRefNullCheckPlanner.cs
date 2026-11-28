// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using Slot = (bool IsArgument, int Index);

namespace NetWasm.Compiler.Analysis.NullChecks;

internal interface IByRefNullCheckPlanner
{
    // The offsets of the instructions that dereference managed addresses none of which can be null.
    ImmutableHashSet<int> Plan(ValidatedControlFlowGraph controlFlow);
}

// Reading or writing through a managed address throws NullReferenceException where the address is 0, as on Mono AOT,
// so the emitters check each address an instruction dereferences unless this proves it non-null. The address of a
// local, argument, static, array element or field is never null (ldelema, ldflda and unbox check their own object or
// address), nor is Unsafe's retyping of such an address, nor the data reference of an array; an offset from one can be
// 0, so offsetting proves nothing. A local
// or argument whose address an instruction dereferenced holds a non-null one afterwards: in that block until stored,
// and in the blocks that block dominates when the method never stores it. A local stored once, with a non-null
// address, in a block that dominates the access's own holds one there too. A slot whose address the method takes
// can change without a store instruction, so it is not followed. A handler or filter begins where control arrives from
// wherever its protected block throws, so the dominance that carries a block's proofs to others roots each of their
// first blocks: nothing the protected block proved reaches into a handler, and a block reached from both gets only what
// both paths proved. A handler enters only at a block's start, and a handler's entry has none of the values its
// protected block's blocks left on the stack.
internal sealed class ByRefNullCheckPlanner(
    IControlFlowDominanceAnalyzer dominance,
    IStackEffectResolver stackEffects,
    ICalledMethodResolver calledMethods,
    IRuntimeIntrinsicRegistry intrinsics) : IByRefNullCheckPlanner
{
    private readonly IControlFlowDominanceAnalyzer _dominance = dominance ??
        throw new ArgumentNullException(nameof(dominance));
    private readonly IStackEffectResolver _stackEffects = stackEffects ??
        throw new ArgumentNullException(nameof(stackEffects));
    private readonly ICalledMethodResolver _calledMethods = calledMethods ??
        throw new ArgumentNullException(nameof(calledMethods));
    private readonly IRuntimeIntrinsicRegistry _intrinsics = intrinsics ??
        throw new ArgumentNullException(nameof(intrinsics));

    public ImmutableHashSet<int> Plan(ValidatedControlFlowGraph controlFlow)
    {
        ArgumentNullException.ThrowIfNull(controlFlow);
        var graph = controlFlow.Graph;
        var body = graph.MethodBody;
        var exposed = body.Instructions
            .Where(static instruction => instruction.Operation is CilOperation.LoadLocalAddress or CilOperation.LoadArgumentAddress)
            .Select(static instruction => new Slot(instruction.Operation == CilOperation.LoadArgumentAddress,
                ((CilOperand.Index)instruction.Operand!).Value))
            .ToHashSet();
        var stores = body.Instructions.Select(Stored).OfType<Slot>().GroupBy(static slot => slot)
            .ToDictionary(static group => group.Key, static group => group.Count());
        var state = new PlanState(exposed, stores);
        // What each planned block leaves on the stack, for the blocks it enters.
        var exits = new Dictionary<int, List<Origin>>();
        foreach (var block in graph.Blocks.Where(block => graph.ReachableBlocks.Contains(block.Index)))
        {
            exits[block.Index] = PlanBlock(controlFlow, block, state, Entry(controlFlow, block, exits));
        }
        if (state.Unproven.Count != 0 && (state.Dereferenced.Count != 0 || state.NonNullStores.Count != 0))
        {
            // A slot loaded before the block stored it holds what it held when the block began: non-null when a
            // block that dominates this one dereferenced it and nothing stores it, or stored its one non-null value.
            var dominators = _dominance.Analyze(graph, graph.Entry.Index, graph.ReachableBlocks);
            foreach (var (block, offset, slots) in state.Unproven)
            {
                if (slots.All(slot => ProvenOnEntry(slot, block, dominators[block], state))) state.Proven.Add(offset);
            }
        }
        return state.Proven.ToImmutable();
    }

    // A value a block starts with on its stack is non-null when every block entering it, each planned already, left a
    // non-null one there: a stack slot carries its value along each edge, as an element's address does past the check
    // of an index read between it and its store. Anything else is unknown.
    private static List<Origin> Entry(ValidatedControlFlowGraph controlFlow, BasicBlock block, Dictionary<int, List<Origin>> exits)
    {
        var depth = controlFlow.InstructionEntryStacks.TryGetValue(block.Instructions[0].Offset, out var stack) ? stack.Length : 0;
        var entry = Enumerable.Repeat(Origin.Unknown, depth).ToList();
        var predecessors = controlFlow.Graph.Predecessors[block.Index];
        if (depth == 0 || predecessors.Length == 0 ||
            predecessors.Any(predecessor => !exits.TryGetValue(predecessor, out var exit) || exit.Count != depth))
        {
            return entry;
        }
        for (var slot = 0; slot < depth; slot++)
        {
            if (predecessors.All(predecessor => exits[predecessor][slot].Kind == OriginKind.NonNull)) entry[slot] = Origin.NonNull;
        }
        return entry;
    }

    private List<Origin> PlanBlock(ValidatedControlFlowGraph controlFlow, BasicBlock block, PlanState state, List<Origin> origins)
    {
        // What this block last stored to each slot, and the slots it dereferenced since.
        var stored = new Dictionary<Slot, Origin>();
        var dereferenced = new HashSet<Slot>();
        for (var index = 0; index < block.Instructions.Length; index++)
        {
            var instruction = block.Instructions[index];
            if (!controlFlow.InstructionEntryStacks.TryGetValue(instruction.Offset, out var stack))
            {
                origins.Clear();
                continue;
            }
            if (origins.Count != stack.Length)
            {
                origins.Clear();
                origins.AddRange(Enumerable.Repeat(Origin.Unknown, stack.Length));
            }
            // An unaligned access is not checked, so it needs no proof and gives none.
            var depths = CilAddressAccesses.IsUnaligned(block.Instructions, index) ? [] : CilAddressAccesses.Depths(instruction.Operation)
                .Where(depth => stack[^depth] == CliValueKind.ManagedAddress)
                .ToArray();
            if (depths.Length != 0)
            {
                Classify(block.Index, instruction.Offset, depths.Select(depth => origins[^depth]), state);
                // An access through a null address throws, so past it the slot it read holds a non-null one.
                foreach (var depth in depths)
                {
                    // A slot this block stored holds the address the access went through, now known non-null, as a
                    // ref local foreach sets is read for each field.
                    if (origins[^depth] is { Kind: OriginKind.Stored, Slot: var written })
                    {
                        stored[written] = Origin.NonNull;
                        continue;
                    }
                    if (origins[^depth] is not { Kind: OriginKind.Loaded, Slot: var slot }) continue;
                    dereferenced.Add(slot);
                    if (!state.Stores.ContainsKey(slot)) state.Dereferenced.Add((block.Index, slot));
                }
            }
            Track(instruction, origins, stored, dereferenced, block.Index, state);
        }
        return origins;
    }

    private static void Classify(int block, int offset, IEnumerable<Origin> addresses, PlanState state)
    {
        var slots = new List<Slot>();
        foreach (var address in addresses)
        {
            switch (address.Kind)
            {
                case OriginKind.NonNull:
                    continue;
                case OriginKind.Loaded:
                    slots.Add(address.Slot);
                    continue;
                default:
                    return;
            }
        }
        if (slots.Count == 0) state.Proven.Add(offset);
        else state.Unproven.Add((block, offset, slots));
    }

    private static bool ProvenOnEntry(Slot slot, int block, DominatorSet dominators, PlanState state) =>
        state.Dereferenced.Any(check => check.Slot == slot && check.Block != block && dominators.Contains(check.Block)) ||
        state.NonNullStores.TryGetValue(slot, out var storeBlock) && storeBlock != block && dominators.Contains(storeBlock);

    private void Track(
        CilInstruction instruction,
        List<Origin> origins,
        Dictionary<Slot, Origin> stored,
        HashSet<Slot> dereferenced,
        int block,
        PlanState state)
    {
        // A branch only pops its operands; the values beneath them go on to the block it enters.
        if (BranchOperands(instruction.Operation) is { } operands)
        {
            if (origins.Count < operands)
            {
                origins.Clear();
                return;
            }
            origins.RemoveRange(origins.Count - operands, operands);
            return;
        }
        if (_stackEffects.Resolve(instruction) is not var (pops, pushes))
        {
            // An instruction this does not follow ends what the tracked stack can say.
            origins.Clear();
            return;
        }
        switch (instruction.Operation)
        {
            case CilOperation.LoadLocal or CilOperation.LoadArgument:
                var loaded = Loaded(instruction);
                origins.Add(state.Exposed.Contains(loaded) ? Origin.Unknown
                    : stored.TryGetValue(loaded, out var held) ? held.Kind == OriginKind.NonNull ? held : new Origin(OriginKind.Stored, loaded)
                    : dereferenced.Contains(loaded) ? Origin.NonNull
                    : new Origin(OriginKind.Loaded, loaded));
                return;
            case CilOperation.StoreLocal or CilOperation.StoreArgument:
                var slot = Stored(instruction)!.Value;
                var value = origins[^1];
                origins.RemoveAt(origins.Count - 1);
                var nonNull = value.Kind == OriginKind.NonNull && !state.Exposed.Contains(slot);
                stored[slot] = nonNull ? Origin.NonNull : Origin.Unknown;
                dereferenced.Remove(slot);
                if (nonNull && state.Stores[slot] == 1) state.NonNullStores[slot] = block;
                // A value loaded from the slot before the store still holds the old one.
                for (var index = 0; index < origins.Count; index++)
                {
                    if (origins[index] is { Kind: OriginKind.Loaded or OriginKind.Stored } old && old.Slot == slot) origins[index] = Origin.Unknown;
                }
                return;
            case CilOperation.Duplicate:
                origins.Add(origins[^1]);
                return;
        }
        var result = Pushed(instruction, origins);
        origins.RemoveRange(origins.Count - pops, pops);
        for (var push = 0; push < pushes; push++) origins.Add(result);
    }

    // What an instruction's result is: the address of a local, argument, static, array element or field, or of the
    // value a box holds, is never null; Unsafe's retyping keeps its address's; an array's data reference is never null,
    // the call having checked the array, nor is an address NonZeroAddress marks, which loop versioning's test covered.
    // Offsetting does not keep it: an access throws where the address it computes is 0, which an offset from a non-null
    // address can reach.
    private Origin Pushed(CilInstruction instruction, List<Origin> origins)
    {
        switch (instruction.Operation)
        {
            case CilOperation.LoadLocalAddress or CilOperation.LoadArgumentAddress or
                CilOperation.LoadStaticFieldAddress or CilOperation.LoadArrayElementAddress or
                CilOperation.LoadFieldAddress or CilOperation.Unbox:
                return Origin.NonNull;
            case CilOperation.Call when _calledMethods.Resolve(instruction) is { } callee &&
                _intrinsics.TryGetIntrinsic(callee.Definition.Key, out var intrinsic):
                var parameters = callee.Signature.ParameterTypes.Length;
                return intrinsic switch
                {
                    RuntimeIntrinsic.GetArrayDataReference or RuntimeIntrinsic.NonZeroAddress => Origin.NonNull,
                    RuntimeIntrinsic.UnsafeAs or RuntimeIntrinsic.UnsafeAsRefManaged when parameters >= 1 && origins.Count >= parameters &&
                        origins[^parameters].Kind == OriginKind.NonNull => Origin.NonNull,
                    _ => Origin.Unknown,
                };
            default:
                return Origin.Unknown;
        }
    }

    private static int? BranchOperands(CilOperation operation) => operation switch
    {
        CilOperation.Branch => 0,
        CilOperation.BranchIfTrue or CilOperation.BranchIfFalse or CilOperation.Switch => 1,
        >= CilOperation.BranchIfEqual and <= CilOperation.BranchIfLessThanOrEqualUnsigned => 2,
        _ => null,
    };

    // The argument or local an ldarg or ldloc reads.
    private static Slot Loaded(CilInstruction instruction) =>
        new(instruction.Operation == CilOperation.LoadArgument, ((CilOperand.Index)instruction.Operand!).Value);

    private static Slot? Stored(CilInstruction instruction) => instruction switch
    {
        { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index local } => new(false, local.Value),
        { Operation: CilOperation.StoreArgument, Operand: CilOperand.Index argument } => new(true, argument.Value),
        _ => null,
    };

    private sealed class PlanState(HashSet<Slot> exposed, Dictionary<Slot, int> stores)
    {
        public HashSet<Slot> Exposed { get; } = exposed;
        // How many instructions store each slot.
        public Dictionary<Slot, int> Stores { get; } = stores;
        public ImmutableHashSet<int>.Builder Proven { get; } = ImmutableHashSet.CreateBuilder<int>();
        // The blocks that dereferenced the address a slot nothing stores holds.
        public HashSet<(int Block, Slot Slot)> Dereferenced { get; } = [];
        // Slots stored once, with a non-null address, by block.
        public Dictionary<Slot, int> NonNullStores { get; } = [];
        // Accesses whose addresses are non-null or slots loaded before their block stored them.
        public List<(int Block, int Offset, List<Slot> Slots)> Unproven { get; } = [];
    }

    private enum OriginKind
    {
        Unknown,
        NonNull,
        // A local or argument the block has not stored or dereferenced.
        Loaded,
        // A local or argument the block stored a value not known non-null to.
        Stored,
    }

    private readonly record struct Origin(OriginKind Kind, Slot Slot)
    {
        public static Origin Unknown => default;
        public static Origin NonNull => new(OriginKind.NonNull, default);
    }
}
