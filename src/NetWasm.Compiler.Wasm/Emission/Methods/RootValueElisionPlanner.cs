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

internal sealed class RootValueElisionPlanner : IRootValueElisionPlanner
{
    public ImmutableDictionary<int, ImmutableHashSet<int>> Plan(StructuredMethod method, MethodEmissionContext context)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(context);
        var result = ImmutableDictionary.CreateBuilder<int, ImmutableHashSet<int>>();
        foreach (var block in method.Blocks.Values)
        {
            var published = new Dictionary<int, RootSource>();
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
                    var proof = skips.ToImmutable();
                    result[instruction.Offset] = result.TryGetValue(instruction.Offset, out var existing)
                        ? existing.Intersect(proof) : proof;
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
        }
        return result.ToImmutable();
    }

    private static bool IsStable(RootSource source, MethodEmissionContext context) =>
        !source.IsIndirect && source.Kind is RootSourceKind.Argument or RootSourceKind.Local &&
        !context.AuthoritativeRootSlots.ContainsKey(source) &&
        !context.FilterEnvironment.Captures.ContainsKey(new(source.Kind == RootSourceKind.Argument, source.Index)) &&
        (source.Kind == RootSourceKind.Argument
            ? !context.ValueLayout.ArgumentOffsets.ContainsKey(source.Index)
            : !context.ValueLayout.SpilledScalarLocals.Contains(source.Index));
}
