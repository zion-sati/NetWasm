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

internal sealed class RootClearElisionPlanner(IStructuredBlockPredecessorReader predecessors) : IRootClearElisionPlanner
{
    private readonly IStructuredBlockPredecessorReader _predecessors =
        predecessors ?? throw new ArgumentNullException(nameof(predecessors));

    public ImmutableDictionary<(int Offset, bool ConstructorCall), ImmutableHashSet<int>> Plan(
        StructuredMethod method, MethodEmissionContext context)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(context);
        var result = ImmutableDictionary.CreateBuilder<(int Offset, bool ConstructorCall), ImmutableHashSet<int>>();
        var roots = context.RootMap;
        if (roots.SlotCount == 0) return result.ToImmutable();
        var all = Enumerable.Range(0, roots.SlotCount).ToImmutableHashSet();
        // A slot zero at the end of every ordinary predecessor is zero at a block's entry. A root
        // frame starts with whatever earlier frames left, so nothing is known at the method's entry,
        // at an opaque block's, or anywhere when the frame is entered inside a terminal arm.
        var entries = context.TerminalBranchValueFramePlacement is { Plan.DefersRootFrame: true }
            ? null
            : RootFactFlow.Solve(method, _predecessors.Read(method), ImmutableHashSet<int>.Empty,
                (left, right) => left.Intersect(right), (left, right) => left.SetEquals(right),
                (block, zero) => Transfer(block, zero, record: false));
        foreach (var (id, block) in method.Blocks)
            Transfer(block, entries?[id] ?? ImmutableHashSet<int>.Empty, record: true);
        return result.ToImmutable();

        ImmutableHashSet<int> Transfer(StructuredBlockDefinition block, ImmutableHashSet<int> zero, bool record)
        {
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
                    if (record) Record((instruction.Offset, false), zero.Intersect(inactive));
                    // An allocation that bumps its run inline publishes only when it
                    // calls the collector: a slot is zero afterwards only if it was
                    // before. The constructor call's publication runs either way.
                    var conditional = context.ConditionalAllocationRoots &&
                        instruction.Operation == CilOperation.NewObject;
                    if (record && !point.ConstructorCallRoots.IsEmpty)
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
                // A stored byref argument's slot is written too.
                if (assigned is { Kind: RootSourceKind.Argument } argument && context.AuthoritativeRootSlots.TryGetValue(
                        argument with { Kind = RootSourceKind.ManagedAddressArgument }, out var byrefSlot))
                    zero = zero.Remove(byrefSlot);
            }
            return zero;
        }

        void Record((int Offset, bool ConstructorCall) site, ImmutableHashSet<int> slots) =>
            result[site] = result.TryGetValue(site, out var existing) ? existing.Intersect(slots) : slots;
    }
}
