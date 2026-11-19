// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal interface IRootValueElisionPlanner
{
    ImmutableDictionary<int, ImmutableHashSet<int>> Plan(StructuredMethod method, MethodEmissionContext context);
}

internal sealed class RootValueElisionPlanner(IStructuredBlockPredecessorReader predecessors) : IRootValueElisionPlanner
{
    private readonly IStructuredBlockPredecessorReader _predecessors =
        predecessors ?? throw new ArgumentNullException(nameof(predecessors));

    // Whether two exits hold the same value in every slot.
    internal static bool HoldTheSameValues(
        ImmutableDictionary<int, RootSource> left, ImmutableDictionary<int, RootSource> right) =>
        left.Count == right.Count &&
        left.All(pair => right.TryGetValue(pair.Key, out var other) && other == pair.Value);

    public ImmutableDictionary<int, ImmutableHashSet<int>> Plan(StructuredMethod method, MethodEmissionContext context)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(context);
        var result = ImmutableDictionary.CreateBuilder<int, ImmutableHashSet<int>>();
        // A slot holding the same stable variable's value at the end of every ordinary predecessor
        // holds it at a block's entry. Nothing is known at the method's entry, at an opaque block's,
        // or anywhere when the root frame is entered inside a terminal arm.
        var empty = ImmutableDictionary<int, RootSource>.Empty;
        var entries = context.TerminalBranchValueFramePlacement is { Plan.DefersRootFrame: true }
            ? null
            : RootFactFlow.Solve(method, _predecessors.Read(method), empty,
                (left, right) => left.Where(pair => right.TryGetValue(pair.Key, out var other) && other == pair.Value)
                    .ToImmutableDictionary(),
                HoldTheSameValues,
                (block, published) => Transfer(block, published, record: false));
        foreach (var (id, block) in method.Blocks)
            Transfer(block, entries?[id] ?? empty, record: true);
        return result.ToImmutable();

        ImmutableDictionary<int, RootSource> Transfer(
            StructuredBlockDefinition block, ImmutableDictionary<int, RootSource> entry, bool record)
        {
            var published = new Dictionary<int, RootSource>(entry);
            var instructions = block.Exit is StructuredTerminalExit terminal
                ? block.Instructions.Add(terminal.Instruction) : block.Instructions;
            for (var position = 0; position < instructions.Length; position++)
            {
                var instruction = instructions[position];
                if (context.ManagedAggregateValues.Plan.Accesses.ContainsKey(instruction.Offset))
                {
                    published.Clear();
                    continue;
                }
                if (context.RootMap.Safepoints.TryGetValue(instruction.Offset, out var point))
                {
                    var skips = ImmutableHashSet.CreateBuilder<int>();
                    var next = new Dictionary<int, RootSource>();
                    if (point.ConstructorCallRoots.IsEmpty)
                    {
                        foreach (var source in point.Roots)
                        {
                            if (!IsStable(source, context)) continue;
                            var slot = context.RootMap.Slots[source];
                            if (published.TryGetValue(slot, out var previous) && previous == source)
                                skips.Add(slot);
                            next.Add(slot, source);
                        }
                    }
                    if (record)
                    {
                        var proof = skips.ToImmutable();
                        result[instruction.Offset] = result.TryGetValue(instruction.Offset, out var existing)
                            ? existing.Intersect(proof) : proof;
                    }
                    // An allocation that bumps its run inline publishes only when it
                    // calls the collector, so a slot keeps a known value afterwards
                    // only if it held that value before.
                    published = context.ConditionalAllocationRoots && instruction.Operation == CilOperation.NewObject
                        ? next.Where(pair => published.TryGetValue(pair.Key, out var before) && before == pair.Value)
                            .ToDictionary(pair => pair.Key, pair => pair.Value)
                        : next;
                }
                RootSource? assigned = instruction.Operation switch
                {
                    CilOperation.StoreLocal => new(RootSourceKind.Local, CilOperandReader.GetIndex(instruction)),
                    CilOperation.StoreArgument => new(RootSourceKind.Argument, CilOperandReader.GetIndex(instruction)),
                    _ => null,
                };
                if (assigned is { } value && context.RootMap.Slots.TryGetValue(value, out var assignedSlot))
                    published.Remove(assignedSlot);
                // An evaluation-stack value stays until the stack drops to its position.
                var kept = KeptStackPositions(method, instructions, position);
                foreach (var (slot, source) in published.ToArray())
                {
                    if (source.Kind is RootSourceKind.EvaluationStack or RootSourceKind.ManagedAddressEvaluationStack &&
                        source.Index >= kept)
                        published.Remove(slot);
                }
            }
            return published.ToImmutableDictionary();
        }
    }

    // The evaluation-stack positions an instruction leaves holding the values they held before it. A CIL
    // instruction pushes at most one value, except dup, which pushes the value it pops back with a copy. So a
    // taller stack after it means only pushes (every earlier position kept); otherwise every position below the
    // height after it is kept, less the one it may have pushed into unless it pushes nothing (a store, pop, nop
    // or call without a result). A block's last instruction is followed by another block's, so it is taken to
    // pop as much as a branch can (two). Without heights (an instruction no path reaches), nothing is kept.
    internal static int KeptStackPositions(StructuredMethod method, ImmutableArray<CilInstruction> instructions, int position)
    {
        var instruction = instructions[position];
        if (!method.InstructionEntryStacks.TryGetValue(instruction.Offset, out var before)) return 0;
        if (position + 1 >= instructions.Length ||
            !method.InstructionEntryStacks.TryGetValue(instructions[position + 1].Offset, out var after))
            return Math.Max(0, before.Length - 2);
        if (after.Length > before.Length) return before.Length;
        return Math.Max(0, PushesNothing(instruction) ? after.Length : after.Length - 1);
    }

    private static bool PushesNothing(CilInstruction instruction) => instruction.Operation switch
    {
        CilOperation.Nop or CilOperation.Pop or CilOperation.StoreLocal or CilOperation.StoreArgument or
            CilOperation.StoreField or CilOperation.StoreStaticField => true,
        CilOperation.Call or CilOperation.CallVirtual => instruction.Operand is CilOperand.MethodInstance
        {
            Value.Signature.ReturnSignatureType.StackKind: CliValueKind.Void,
        },
        _ => false,
    };

    // A source whose published value stays current until a write the planner sees: an argument or a local
    // held in a Wasm local (written by starg and stloc), or an evaluation-stack value (written only by pushes to
    // its position). A local holding a byref is not one: the emitter rebinds some pointers outside stloc.
    private static bool IsStable(RootSource source, MethodEmissionContext context) => !source.IsIndirect && source.Kind switch
    {
        RootSourceKind.EvaluationStack or RootSourceKind.ManagedAddressEvaluationStack => true,
        RootSourceKind.Argument or RootSourceKind.Local =>
            !context.AuthoritativeRootSlots.ContainsKey(source) &&
            !context.FilterEnvironment.Captures.ContainsKey(new(source.Kind == RootSourceKind.Argument, source.Index)) &&
            (source.Kind == RootSourceKind.Argument
                ? !context.ValueLayout.ArgumentOffsets.ContainsKey(source.Index)
                : !context.ValueLayout.SpilledScalarLocals.Contains(source.Index)),
        _ => false,
    };
}
