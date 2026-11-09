using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using Slot = (bool IsArgument, int Index);

namespace NetWasm.Compiler.Analysis.NullChecks;

internal interface IConstructedReceiverPlanner
{
    // The offsets of the field accesses, callvirts and direct constructor calls
    // whose object cannot be null.
    ImmutableHashSet<int> Plan(ValidatedControlFlowGraph controlFlow, bool thisIsNonNull);
}

// newobj never produces null, so an access of the object it pushed, or of a copy
// dup made, needs no null check; nor does an access through a local or argument
// that the block itself stored such an object to, or that is stored exactly once,
// with such an object, in a block that dominates the access's own and so ran
// before it. A this that the caller has proven non-null, as a constructor's is
// when only newobj and chained constructors call it, counts as such an object
// while the method never stores to it or exposes its address. Exception handlers
// can enter blocks no dominating store reached, so a method with them is left
// alone, as the structured lowering drops its proof anyway.
internal sealed class ConstructedReceiverPlanner(
    IControlFlowDominanceAnalyzer dominance,
    IStackEffectResolver stackEffects,
    ICalledMethodResolver calledMethods) : IConstructedReceiverPlanner
{
    private readonly IControlFlowDominanceAnalyzer _dominance = dominance ??
        throw new ArgumentNullException(nameof(dominance));
    private readonly IStackEffectResolver _stackEffects = stackEffects ??
        throw new ArgumentNullException(nameof(stackEffects));
    private readonly ICalledMethodResolver _calledMethods = calledMethods ??
        throw new ArgumentNullException(nameof(calledMethods));

    public ImmutableHashSet<int> Plan(ValidatedControlFlowGraph controlFlow, bool thisIsNonNull)
    {
        ArgumentNullException.ThrowIfNull(controlFlow);
        var graph = controlFlow.Graph;
        var body = graph.MethodBody;
        if (!body.ExceptionRegions.IsEmpty) return [];
        var exposed = body.Instructions
            .Where(static instruction => instruction.Operation is CilOperation.LoadLocalAddress or CilOperation.LoadArgumentAddress)
            .Select(static instruction => new Slot(instruction.Operation == CilOperation.LoadArgumentAddress,
                ((CilOperand.Index)instruction.Operand).Value))
            .ToHashSet();
        var stores = body.Instructions.Select(Stored).OfType<Slot>().GroupBy(static slot => slot)
            .ToDictionary(static group => group.Key, static group => group.Count());
        var thisSlot = new Slot(true, 0);
        var thisHolds = thisIsNonNull && !exposed.Contains(thisSlot) && !stores.ContainsKey(thisSlot);
        var proven = ImmutableHashSet.CreateBuilder<int>();
        // Slots stored once, with a constructed object, by block.
        var constructedStores = new Dictionary<Slot, int>();
        var unproven = new List<(int Block, int Offset, Slot Slot)>();
        foreach (var block in graph.Blocks.Where(block => graph.ReachableBlocks.Contains(block.Index)))
        {
            var origins = new List<Origin>();
            // What this block last stored to each slot.
            var stored = new Dictionary<Slot, Origin>();
            foreach (var instruction in block.Instructions)
            {
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
                if (ReceiverDepth(instruction) is > 0 and var depth && stack[^depth] == CliValueKind.ManagedReference)
                {
                    switch (origins[^depth])
                    {
                        case { Kind: OriginKind.Constructed }:
                        case { Kind: OriginKind.This } when thisHolds:
                            proven.Add(instruction.Offset);
                            break;
                        case { Kind: OriginKind.Loaded, Slot: var slot }:
                            unproven.Add((block.Index, instruction.Offset, slot));
                            break;
                    }
                }
                Track(instruction, origins, stored, block.Index, stores, exposed, constructedStores, thisHolds);
            }
        }
        if (unproven.Count != 0 && constructedStores.Count != 0)
        {
            // A value loaded before the block stored its slot is the one the block
            // entered with: proven when the slot's one store is in a block that
            // dominates this one.
            var dominators = _dominance.Analyze(graph, graph.Entry.Index, graph.ReachableBlocks);
            foreach (var (block, offset, slot) in unproven)
            {
                if (constructedStores.TryGetValue(slot, out var storeBlock) && storeBlock != block &&
                    dominators[block].Contains(storeBlock))
                {
                    proven.Add(offset);
                }
            }
        }
        return proven.ToImmutable();
    }

    // How deep beneath the top of the stack an access's object reference lies. A
    // constructor called directly, as a chained constructor is, takes its object
    // as a receiver too, which tells the constructor prover what this it passes.
    private int ReceiverDepth(CilInstruction instruction) => instruction.Operation switch
    {
        CilOperation.LoadField or CilOperation.LoadFieldAddress => 1,
        CilOperation.StoreField => 2,
        CilOperation.CallVirtual when _calledMethods.Resolve(instruction) is { Definition.IsStatic: false } callee =>
            callee.Signature.ParameterTypes.Length + 1,
        CilOperation.Call when _calledMethods.Resolve(instruction) is { } callee && ConstructorReceivers.IsInstanceConstructor(callee) =>
            callee.Signature.ParameterTypes.Length + 1,
        _ => 0,
    };

    private void Track(
        CilInstruction instruction,
        List<Origin> origins,
        Dictionary<Slot, Origin> stored,
        int block,
        Dictionary<Slot, int> stores,
        HashSet<Slot> exposed,
        Dictionary<Slot, int> constructedStores,
        bool thisHolds)
    {
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
                origins.Add(stored.TryGetValue(loaded, out var held)
                    ? held
                    : loaded == new Slot(true, 0) ? Origin.This : new Origin(OriginKind.Loaded, loaded));
                return;
            case CilOperation.StoreLocal or CilOperation.StoreArgument:
                var slot = Stored(instruction)!.Value;
                var value = origins[^1];
                origins.RemoveAt(origins.Count - 1);
                // A slot exposed by address can change without a store instruction. A
                // proven this stored to a local, as inlining a chained constructor
                // spills it, is as good as a constructed object.
                var constructed = (value.Kind == OriginKind.Constructed || value.Kind == OriginKind.This && thisHolds) &&
                    !exposed.Contains(slot);
                stored[slot] = constructed ? Origin.Constructed : Origin.Unknown;
                if (constructed && stores[slot] == 1) constructedStores[slot] = block;
                // A value loaded from the slot before the store still holds the old one.
                for (var index = 0; index < origins.Count; index++)
                {
                    if (origins[index] is { Kind: OriginKind.Loaded or OriginKind.This } old && old.Slot == slot)
                    {
                        origins[index] = Origin.Unknown;
                    }
                }
                return;
            case CilOperation.Duplicate:
                origins.Add(origins[^1]);
                return;
        }
        origins.RemoveRange(origins.Count - pops, pops);
        for (var push = 0; push < pushes; push++)
        {
            origins.Add(instruction.Operation == CilOperation.NewObject &&
                _calledMethods.Resolve(instruction) is { DeclaringType.IsValueType: false }
                ? Origin.Constructed
                : Origin.Unknown);
        }
    }

    // The argument or local an ldarg or ldloc reads.
    private static Slot Loaded(CilInstruction instruction) =>
        new(instruction.Operation == CilOperation.LoadArgument, ((CilOperand.Index)instruction.Operand!).Value);

    private static Slot? Stored(CilInstruction instruction) => instruction switch
    {
        { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index local } => new(false, local.Value),
        { Operation: CilOperation.StoreArgument, Operand: CilOperand.Index argument } => new(true, argument.Value),
        _ => null,
    };

    private enum OriginKind
    {
        Unknown,
        // An object newobj just produced.
        Constructed,
        // The method's argument 0.
        This,
        // A local or argument the block has not stored.
        Loaded,
    }

    private readonly record struct Origin(OriginKind Kind, Slot Slot)
    {
        public static Origin Unknown => default;
        public static Origin Constructed => new(OriginKind.Constructed, default);
        public static Origin This => new(OriginKind.This, new Slot(true, 0));
    }
}
