using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal interface IRootClearElisionPlanner
{
    ImmutableDictionary<(int Offset, bool ConstructorCall), ImmutableHashSet<int>> Plan(
        StructuredMethod method, MethodEmissionContext context);
}

internal sealed class RootClearElisionPlanner : IRootClearElisionPlanner
{
    public ImmutableDictionary<(int Offset, bool ConstructorCall), ImmutableHashSet<int>> Plan(
        StructuredMethod method, MethodEmissionContext context)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(context);
        var result = ImmutableDictionary.CreateBuilder<(int Offset, bool ConstructorCall), ImmutableHashSet<int>>();
        var roots = context.RootMap;
        if (roots.SlotCount == 0) return result.ToImmutable();
        var all = Enumerable.Range(0, roots.SlotCount).ToImmutableHashSet();
        foreach (var block in method.Blocks.Values)
        {
            // No facts cross block entries, including exception/loop entries.
            var zero = ImmutableHashSet<int>.Empty;
            var instructions = block.Exit is StructuredTerminalExit terminal
                ? block.Instructions.Add(terminal.Instruction) : block.Instructions;
            foreach (var instruction in instructions)
            {
                // This path bypasses the ordinary instruction dispatcher.
                if (context.ManagedAggregateValues.Plan.Accesses.ContainsKey(instruction.Offset)) continue;
                if (roots.Safepoints.TryGetValue(instruction.Offset, out var point))
                {
                    var normalLive = point.Roots.Select(source => roots.Slots[source]).ToImmutableHashSet();
                    var constructorLive = point.ConstructorCallRoots.Select(source => roots.Slots[source]).ToImmutableHashSet();
                    // A repeated publication inside the same instruction may
                    // follow either phase. Never assume its active slots stayed zero.
                    var inactive = all.Except(normalLive).Except(constructorLive);
                    Record((instruction.Offset, false), zero.Intersect(inactive));
                    // An allocation that bumps its run inline publishes only when it
                    // calls the collector: a slot is zero afterwards only if it was
                    // before. The constructor call's publication runs either way.
                    var conditional = context.ConditionalAllocationRoots &&
                        instruction.Operation == CilOperation.NewObject;
                    if (!point.ConstructorCallRoots.IsEmpty)
                        Record((instruction.Offset, true), conditional ? zero.Intersect(inactive) : inactive);
                    zero = conditional && point.ConstructorCallRoots.IsEmpty ? zero.Intersect(inactive) : inactive;
                }
                RootSource? assigned = instruction.Operation switch
                {
                    CilOperation.StoreLocal => new(RootSourceKind.Local, CilOperandReader.GetIndex(instruction)),
                    CilOperation.StoreArgument => new(RootSourceKind.Argument, CilOperandReader.GetIndex(instruction)),
                    _ => null,
                };
                if (assigned is { } source && context.AuthoritativeRootSlots.TryGetValue(source, out var slot))
                    zero = zero.Remove(slot);
            }
        }
        return result.ToImmutable();

        void Record((int Offset, bool ConstructorCall) site, ImmutableHashSet<int> slots) =>
            result[site] = result.TryGetValue(site, out var existing) ? existing.Intersect(slots) : slots;
    }
}
