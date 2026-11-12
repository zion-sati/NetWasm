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
            foreach (var instruction in instructions)
            {
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
            }
            return published.ToImmutableDictionary();
        }
    }

    private static bool IsStable(RootSource source, MethodEmissionContext context) =>
        !source.IsIndirect && source.Kind is RootSourceKind.Argument or RootSourceKind.Local &&
        !context.AuthoritativeRootSlots.ContainsKey(source) &&
        !context.FilterEnvironment.Captures.ContainsKey(new(source.Kind == RootSourceKind.Argument, source.Index)) &&
        (source.Kind == RootSourceKind.Argument
            ? !context.ValueLayout.ArgumentOffsets.ContainsKey(source.Index)
            : !context.ValueLayout.SpilledScalarLocals.Contains(source.Index));
}
