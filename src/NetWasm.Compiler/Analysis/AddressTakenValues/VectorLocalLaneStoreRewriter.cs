// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Analysis.Loops;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.AddressTakenValues;

internal interface IVectorLocalLaneStoreRewriter
{
    CilMethodBody Rewrite(CilMethodBody body);
}

// A System.Numerics vector local or argument whose lane is written through its address gets its value with that
// lane replaced, as RyuJIT inserts the element into a promoted SIMD local. The writes are `v.X = a` (the variable's
// address, the value, stfld), `v.X op= a` as C# compiles it (the lane's address from ldflda, dup, a read through
// it, the value, a store through it, or the variable's address, dup, ldfld, the value, stfld), an inlined receiver's
// `this.Z = a` and a split struct field's store. The variable's address becomes its value viewed as a
// Vector128<float> and the lane's index, the store becomes Vector128.WithElement, the view back and a store to the
// variable, and a read through the copied address reads the lane from the value. A lane read through the lane's
// address reads it from the value too, and a dup of the variable's address whose second copy goes to the next
// access becomes the address taken again there, so each access stands alone.
//
// The value stored is computed between the address and the store, as before, so nothing there may write the
// variable: no store to it, no address of it but one a lane read takes at once, and, where something there could
// write memory, no address of it anywhere the method could have given away. The dereference folder then reads the
// remaining lanes from the value, and a variable left with no address needs no value-frame storage.
internal sealed class VectorLocalLaneStoreRewriter(
    IControlFlowGraphBuilder graphs,
    ITypedStackValidator stacks,
    IVectorLoopMethodResolver methods,
    IVectorLaneFields lanes,
    bool enabled) : IVectorLocalLaneStoreRewriter
{
    // Taking an address again exposes the access after it, so a few rounds settle a sequence of accesses.
    internal const int MaximumRounds = 4;

    private static readonly CliTypeIdentity Single = CliTypeIdentity.Primitive("f4", CliValueKind.F4);

    private readonly IControlFlowGraphBuilder _graphs = graphs ?? throw new ArgumentNullException(nameof(graphs));
    private readonly ITypedStackValidator _stacks = stacks ?? throw new ArgumentNullException(nameof(stacks));
    private readonly IVectorLoopMethodResolver _methods = methods ?? throw new ArgumentNullException(nameof(methods));
    private readonly IVectorLaneFields _lanes = lanes ?? throw new ArgumentNullException(nameof(lanes));

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!enabled || !HasCandidate(body)) return body;
        for (var round = 0; round < MaximumRounds; round++)
        {
            var rewritten = RewriteOnce(body);
            if (ReferenceEquals(rewritten, body)) break;
            body = rewritten;
        }
        return body;
    }

    private CilMethodBody RewriteOnce(CilMethodBody body)
    {
        var entryStacks = _stacks.Validate(_graphs.Build(body)).InstructionEntryStacks;
        var site = new Site(body, entryStacks, new StackSlotProducers(body, entryStacks));
        for (var index = 0; index < body.Instructions.Length; index++)
        {
            var instruction = body.Instructions[index];
            if (index > 0 && body.Instructions[index - 1].Operation is CilOperation.Volatile or CilOperation.Unaligned ||
                !entryStacks.ContainsKey(instruction.Offset))
            {
                continue;
            }
            switch (instruction.Operation)
            {
                case CilOperation.StoreField:
                    FieldStore(site, index);
                    break;
                case CilOperation.StoreObject when IsSingle(instruction):
                    LaneAddressStore(site, index);
                    break;
                case CilOperation.LoadObject when IsSingle(instruction):
                    LaneAddressRead(site, index);
                    break;
                case CilOperation.Duplicate:
                    AddressAgain(site, index);
                    break;
            }
        }
        if (site.Spans.Count == 0) return body;
        site.Spans.Sort(static (left, right) => left.First.CompareTo(right.First));
        return BranchShapes.Splice(body, site.Spans);
    }

    // `v.X = a`, or `v.X op= a` through a dup of v's address and an ldfld just after it.
    private void FieldStore(Site site, int store)
    {
        if (Producer(site, store, 1) is not { } found) return;
        var instructions = site.Body.Instructions;
        if (Variable(site.Body, instructions[found]) is { } variable)
        {
            if (Lane(instructions[store], variable) is not { } lane || !Undisturbed(site, found + 1, store, variable, [])) return;
            Add(site, [Insert(site, found, variable, lane), Stored(site, store, variable)]);
            return;
        }
        if (instructions[found].Operation != CilOperation.Duplicate || found == 0 ||
            site.Producers.FindTop(found, acrossThrowPaths: true) != found - 1 ||
            Variable(site.Body, instructions[found - 1]) is not { } duplicated ||
            Lane(instructions[store], duplicated) is not { } stored ||
            found + 1 >= store || site.Producers.FindTop(found + 1) != found ||
            instructions[found + 1].Operation != CilOperation.LoadField || Lane(instructions[found + 1], duplicated) is null ||
            !Undisturbed(site, found, store, duplicated, [found, found + 1]))
        {
            return;
        }
        Add(site,
        [
            Insert(site, found - 1, duplicated, stored),
            Replace(site, found, duplicated.Load, duplicated.Operand),
            Stored(site, store, duplicated),
        ]);
    }

    // A store through a lane's address: `ldloca v; ldflda X; a; stobj float32`, or C#'s `v.X op= a` through a dup of
    // the lane's address and a read through it just after.
    private void LaneAddressStore(Site site, int store)
    {
        if (Producer(site, store, 1) is not { } found) return;
        var instructions = site.Body.Instructions;
        if (LaneAddress(site, found) is { } direct)
        {
            if (!Undisturbed(site, found + 1, store, direct.Variable, [])) return;
            Add(site,
            [
                Insert(site, found - 1, direct.Variable, direct.Index),
                Removed(site, found),
                Stored(site, store, direct.Variable),
            ]);
            return;
        }
        if (instructions[found].Operation != CilOperation.Duplicate || found == 0 ||
            site.Producers.FindTop(found, acrossThrowPaths: true) != found - 1 ||
            LaneAddress(site, found - 1) is not { } duplicated ||
            found + 1 >= store || site.Producers.FindTop(found + 1) != found ||
            instructions[found + 1].Operation != CilOperation.LoadObject || !IsSingle(instructions[found + 1]) ||
            !Undisturbed(site, found, store, duplicated.Variable, [found, found + 1]))
        {
            return;
        }
        var variable = duplicated.Variable;
        Add(site,
        [
            Insert(site, found - 2, variable, duplicated.Index),
            Removed(site, found - 1),
            Replace(site, found, variable.Load, variable.Operand),
            Replace(site, found + 1, CilOperation.LoadField, instructions[found - 1].Operand),
            Stored(site, store, variable),
        ]);
    }

    // A read through a lane's address just after it reads the lane from the variable's value.
    private void LaneAddressRead(Site site, int read)
    {
        if (site.Producers.FindTop(read) is not { } found || found != read - 1 || LaneAddress(site, found) is not { } lane) return;
        Add(site,
        [
            Replace(site, found - 1, lane.Variable.Load, lane.Variable.Operand),
            Removed(site, found),
            Replace(site, read, CilOperation.LoadField, site.Body.Instructions[found].Operand),
        ]);
    }

    // A dup of the variable's address whose lower copy is back on top of the stack once the upper copy's access is
    // done: the address taken again there instead, which is the same address in the same stack slot.
    private static void AddressAgain(Site site, int duplicate)
    {
        var instructions = site.Body.Instructions;
        if (duplicate == 0 || site.Producers.FindTop(duplicate, acrossThrowPaths: true) != duplicate - 1 ||
            Variable(site.Body, instructions[duplicate - 1]) is not { } variable ||
            !site.Stacks.TryGetValue(instructions[duplicate].Offset, out var entry))
        {
            return;
        }
        var lower = entry.Length - 1;
        for (var index = duplicate + 1; index < instructions.Length; index++)
        {
            if (site.Producers.Enters(index) ||
                !site.Stacks.TryGetValue(instructions[index].Offset, out var stack) ||
                stack.Length <= lower || Find(site, index, lower) != duplicate)
            {
                return;
            }
            if (stack.Length != lower + 1) continue;
            Add(site,
            [
                Removed(site, duplicate),
                new(index, index,
                [
                    BranchShapes.Synthetic(instructions[index], variable.Address, variable.Operand),
                    instructions[index],
                ]),
            ]);
            return;
        }
    }

    // The lane's address an ldflda takes from the variable's address just before it.
    private LaneOf? LaneAddress(Site site, int address)
    {
        var instructions = site.Body.Instructions;
        return address > 0 && instructions[address].Operation == CilOperation.LoadFieldAddress &&
            site.Producers.FindTop(address) == address - 1 &&
            Variable(site.Body, instructions[address - 1]) is { } variable &&
            Lane(instructions[address], variable) is { } lane
                ? new(variable, lane)
                : null;
    }

    private int? Lane(CilInstruction access, VariableAccess variable) => _lanes.Lane(access.Operand, variable.Type);

    // The variable's address becomes its value viewed as Vector128<float> and the lane's index.
    private BranchShapes.Span Insert(Site site, int address, VariableAccess variable, int lane)
    {
        var source = site.Body.Instructions[address];
        return new(address, address,
        [
            BranchShapes.Synthetic(source, variable.Load, variable.Operand),
            BranchShapes.Synthetic(source, CilOperation.Call,
                new CilOperand.MethodInstance(_methods.Resolve(VectorLoopMethod.ToVector128(variable.Type.FullName!), Single))),
            BranchShapes.Synthetic(source, CilOperation.LoadInt32, new CilOperand.ConstantI4(lane)),
        ]);
    }

    // The store becomes the lane's insertion, the view back and a store to the variable.
    private BranchShapes.Span Stored(Site site, int store, VariableAccess variable)
    {
        var source = site.Body.Instructions[store];
        return new(store, store,
        [
            BranchShapes.Synthetic(source, CilOperation.Call,
                new CilOperand.MethodInstance(_methods.Resolve(VectorLoopMethod.WithElement, Single))),
            BranchShapes.Synthetic(source, CilOperation.Call,
                new CilOperand.MethodInstance(_methods.Resolve(VectorLoopMethod.FromVector128(variable.Type.FullName!), Single))),
            BranchShapes.Synthetic(source, variable.Store, variable.Operand),
        ]);
    }

    private static BranchShapes.Span Replace(Site site, int index, CilOperation operation, CilOperand operand) =>
        new(index, index, [BranchShapes.Synthetic(site.Body.Instructions[index], operation, operand)]);

    private static BranchShapes.Span Removed(Site site, int index) =>
        Replace(site, index, CilOperation.Nop, new CilOperand.None());

    // Whether an ldobj or stobj, which names its type, accesses a float.
    private static bool IsSingle(CilInstruction access) =>
        ((CilOperand.TypeIdentity)access.Operand).Value.CanonicalName == "primitive:f4";

    private static bool HasCandidate(CilMethodBody body) =>
        body.Instructions.Any(instruction => Variable(body, instruction) is not null);

    // The vector local or argument an ldloca or ldarga addresses, with how it is loaded, stored and addressed; an
    // instance method's `this` is already an address, so its own address is left alone.
    private static VariableAccess? Variable(CilMethodBody body, CilInstruction address)
    {
        switch (address)
        {
            case { Operation: CilOperation.LoadLocalAddress, Operand: CilOperand.Index { Value: var local } }
                when (uint)local < (uint)body.LocalSignatureTypes.Length &&
                    VectorLaneFields.Lanes(body.LocalSignatureTypes[local]) is not null:
                return new(local, body.LocalSignatureTypes[local], CilOperation.LoadLocal, CilOperation.StoreLocal,
                    CilOperation.LoadLocalAddress);
            case { Operation: CilOperation.LoadArgumentAddress, Operand: CilOperand.Index { Value: var argument } }:
                var parameter = body.Method.IsStatic ? argument : argument - 1;
                var parameters = (body.MethodInstance?.Signature ?? body.Method.Signature).ParameterSignatureTypes;
                return (uint)parameter < (uint)parameters.Length && VectorLaneFields.Lanes(parameters[parameter]) is not null
                    ? new(argument, parameters[parameter], CilOperation.LoadArgument, CilOperation.StoreArgument,
                        CilOperation.LoadArgumentAddress)
                    : null;
            default:
                return null;
        }
    }

    private sealed record VariableAccess(
        int Index, CliTypeIdentity Type, CilOperation Load, CilOperation Store, CilOperation Address)
    {
        public CilOperand Operand => new CilOperand.Index(Index);
    }

    private sealed record LaneOf(VariableAccess Variable, int Index);

    // One round's body, its stacks, the producers of its values, the replacements found so far and which variables
    // give their address away.
    private sealed class Site(
        CilMethodBody body, ImmutableDictionary<int, ImmutableArray<CliValueKind>> stacks, StackSlotProducers producers)
    {
        public CilMethodBody Body { get; } = body;

        public ImmutableDictionary<int, ImmutableArray<CliValueKind>> Stacks { get; } = stacks;

        public StackSlotProducers Producers { get; } = producers;

        public List<BranchShapes.Span> Spans { get; } = [];

        public HashSet<int> Replaced { get; } = [];

        public HashSet<(CilOperation, int)>? Escaped { get; set; }
    }

    private static int? Find(Site site, int consumer, int slot) =>
        site.Producers.Find(consumer, slot, duplicates: true, acrossThrowPaths: true);

    // The producer of the value `depth` slots below the top of the instruction's entry stack, which holds what the
    // instruction pops; none where no path reaches the instruction.
    private static int? Producer(Site site, int consumer, int depth) =>
        site.Stacks.TryGetValue(site.Body.Instructions[consumer].Offset, out var stack)
            ? Find(site, consumer, stack.Length - 1 - depth)
            : null;

    // The replacements of one access, unless another access already replaced one of its instructions.
    private static void Add(Site site, BranchShapes.Span[] spans)
    {
        if (spans.Any(span => site.Replaced.Contains(span.First))) return;
        foreach (var span in spans)
        {
            site.Replaced.Add(span.First);
            site.Spans.Add(span);
        }
    }

    // Whether nothing in [first, store) but the skipped instructions can write the variable: no store to it, no
    // address of it but one a lane read takes at once, and nothing that writes memory unless the method keeps every
    // address of the variable to itself.
    private static bool Undisturbed(Site site, int first, int store, VariableAccess variable, int[] skipped)
    {
        var instructions = site.Body.Instructions;
        var writesMemory = false;
        for (var index = first; index < store; index++)
        {
            if (skipped.Contains(index)) continue;
            var instruction = instructions[index];
            if (instruction.Operand is CilOperand.Index { Value: var slot } && slot == variable.Index &&
                (instruction.Operation == variable.Store || instruction.Operation == variable.Address))
            {
                // `v.Y = v.X + a` reads a lane through a second address.
                if (instruction.Operation == variable.Address && index + 1 < store &&
                    instructions[index + 1].Operation == CilOperation.LoadField &&
                    site.Producers.FindTop(index + 1) == index)
                {
                    index++;
                    continue;
                }
                return false;
            }
            writesMemory |= instruction.Operation is CilOperation.Call or CilOperation.CallVirtual or
                CilOperation.CallIndirect or CilOperation.NewObject or CilOperation.StoreObject or
                CilOperation.StoreField or CilOperation.CopyObject or CilOperation.InitializeObject or
                CilOperation.CopyBlock or CilOperation.InitializeBlock;
        }
        return !writesMemory || !Escaped(site).Contains((variable.Address, variable.Index));
    }

    // The variables some address of which goes anywhere but into field reads and writes, indirect reads and writes,
    // initobj, a lane's address or a dup whose copies go there: a call, a ref local or a pointer could then write the
    // variable later.
    private static HashSet<(CilOperation, int)> Escaped(Site site)
    {
        if (site.Escaped is { } known) return known;
        var instructions = site.Body.Instructions;
        var consumers = new Dictionary<int, List<int>>();
        for (var index = 0; index < instructions.Length; index++)
        {
            var depth = instructions[index].Operation switch
            {
                CilOperation.LoadField or CilOperation.LoadObject or CilOperation.InitializeObject or
                    CilOperation.LoadFieldAddress or CilOperation.Duplicate => 0,
                CilOperation.StoreField or CilOperation.StoreObject => 1,
                _ => -1,
            };
            if (depth < 0 || Producer(site, index, depth) is not { } producer) continue;
            (consumers.TryGetValue(producer, out var list) ? list : consumers[producer] = []).Add(index);
        }
        bool Kept(int producer) =>
            consumers.TryGetValue(producer, out var list) &&
            list.Count == (instructions[producer].Operation == CilOperation.Duplicate ? 2 : 1) &&
            list.All(consumer => instructions[consumer].Operation is not
                (CilOperation.LoadFieldAddress or CilOperation.Duplicate) || Kept(consumer));
        var escaped = new HashSet<(CilOperation, int)>();
        for (var index = 0; index < instructions.Length; index++)
        {
            if (instructions[index] is
                {
                    Operation: CilOperation.LoadLocalAddress or CilOperation.LoadArgumentAddress,
                    Operand: CilOperand.Index { Value: var slot },
                } address && !Kept(index))
            {
                escaped.Add((address.Operation, slot));
            }
        }
        site.Escaped = escaped;
        return escaped;
    }
}
