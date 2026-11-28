// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Analysis.Arrays;
using NetWasm.Compiler.Analysis.NullChecks;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Loops;

internal interface IByRefLoopVersioner
{
    CilMethodBody Rewrite(CilMethodBody body);
}

// An access through a managed address throws NullReferenceException where its address is 0, so a span loop checks
// each element's address, two instructions per access (a string hash over a span 35% more than without them, an in-place
// prefix sum 87%). A loop entered at its condition `i < bound`, whose counter i (an int or native int local nothing
// takes the address of) it writes once as `i = i + c` with c > 0, and whose bound reads only constants and locals and
// arguments it does not write, runs its body only while i < bound: before the update, every read of Unsafe.Add(ref r,
// i + k), with r a reference the loop does not write, lies between r's elements i0 + kmin and bound - 1 + kmax, where i0
// is the counter on entry and kmin and kmax the reads' least and greatest k. Ahead of the loop a test of that run for
// each reference and element type (NonZeroAddressRange: the first not 0, the last not past the top of the address space,
// i0 + kmin not negative and, where the index is sign-extended, bound - 1 + kmax not past int.MaxValue) picks a copy of
// the loop whose reads NonZeroAddress marks, so their accesses need no check; where a test fails the loop runs as it
// was and throws where O0 does. The copy is the loop instruction for instruction but for the marks, so every level
// matches O0. It runs before address induction, which then moves both loops' reads onto references of their own and
// keeps the marks. Innermost loops only, entered nowhere else and within the array versioner's copy budget; methods with
// protected regions are left alone, as the null-check proof leaves them.
internal sealed class ByRefLoopVersioner(
    ICalledMethodResolver calledMethods,
    IRuntimeIntrinsicRegistry intrinsics,
    IVectorLoopMethodResolver methods,
    bool enabled) : IByRefLoopVersioner
{
    // The deepest the tests stack values, past what the bound needs.
    private const int TestStack = 6;

    private readonly ICalledMethodResolver _calledMethods = calledMethods ?? throw new ArgumentNullException(nameof(calledMethods));
    private readonly IRuntimeIntrinsicRegistry _intrinsics = intrinsics ?? throw new ArgumentNullException(nameof(intrinsics));
    private readonly IVectorLoopMethodResolver _methods = methods ?? throw new ArgumentNullException(nameof(methods));

    // A read of base plus the counter and Offset: Base loads the reference, Add is the call and Conversion, if any, the
    // conversion of the index just before it.
    private sealed record Read(int Base, int Add, int? Conversion, int Offset);

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var instructions = body.Instructions;
        if (!enabled || !body.ExceptionRegions.IsEmpty ||
            instructions.Any(static instruction => instruction.Operand is CilOperand.SwitchTargets) ||
            !instructions.Any(instruction => Intrinsic(instruction) == RuntimeIntrinsic.UnsafeAdd))
        {
            return body;
        }
        var indices = Enumerable.Range(0, instructions.Length).ToDictionary(index => instructions[index].Offset);
        var targets = instructions
            .Select(static instruction => instruction.Operand)
            .OfType<CilOperand.BranchTarget>()
            .Select(static target => target.Offset)
            .ToHashSet();
        var addressed = instructions
            .Where(static instruction => instruction.Operation is CilOperation.LoadLocalAddress or CilOperation.LoadArgumentAddress)
            .Select(static instruction => (instruction.Operation == CilOperation.LoadArgumentAddress, ((CilOperand.Index)instruction.Operand).Value))
            .ToHashSet();
        // Every branch by its target, so a loop finds the branches into it without scanning the body.
        var branches = Enumerable.Range(0, instructions.Length)
            .Where(index => instructions[index].Operand is CilOperand.BranchTarget)
            .Select(index => (Target: indices[((CilOperand.BranchTarget)instructions[index].Operand).Offset], Source: index))
            .OrderBy(static branch => branch.Target)
            .ToArray();
        var candidates = new List<(int Head, int Condition, int Back, int Counter, CilInstruction[] Bound, List<Read> Reads)>();
        for (var back = 2; back < instructions.Length; back++)
        {
            if (instructions[back] is not { Operation: CilOperation.BranchIfLessThanSigned, Operand: CilOperand.BranchTarget { Offset: var headOffset } } ||
                indices[headOffset] is not (> 0 and var head) || head >= back ||
                instructions[head - 1] is not { Operation: CilOperation.Branch, Operand: CilOperand.BranchTarget { Offset: var conditionOffset } } ||
                !indices.TryGetValue(conditionOffset, out var condition) || condition <= head || condition >= back ||
                back - head + 1 > ArrayLoopVersioner.LoopInstructionBudget ||
                !Enclosed(branches, head, back) ||
                instructions[condition] is not { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var counter } } ||
                body.Locals[counter] is not (CliValueKind.I4 or CliValueKind.NativeInt) ||
                addressed.Contains((false, counter)))
            {
                continue;
            }
            var written = Enumerable.Range(head, back - head)
                .Where(index => instructions[index].Operation is CilOperation.StoreLocal or CilOperation.StoreArgument)
                .Select(index => (Argument: instructions[index].Operation == CilOperation.StoreArgument,
                    Index: ((CilOperand.Index)instructions[index].Operand).Value))
                .ToList();
            var bound = instructions.AsSpan(condition + 1, back - condition - 1).ToArray();
            if (!Invariant(bound, written.ToHashSet(), addressed) ||
                Update(body, head, condition, counter, written, targets) is not { } update)
            {
                continue;
            }
            var reads = Reads(body, head, update, counter, written, addressed, targets, branches);
            if (reads.Count != 0) candidates.Add((head, condition, back, counter, bound, reads));
        }
        if (candidates.Count == 0) return body;

        // A copy leaves the loop's own counter where it started, so no path from the loop's exits may read it first.
        var liveness = CilLocalLiveness.Compute(instructions, indices, candidates.Select(static candidate => candidate.Counter));
        List<CilInstruction>[]? replacements = null;
        var locals = body.Locals.ToBuilder();
        var localTypes = body.LocalSignatureTypes.ToBuilder();
        foreach (var (head, condition, back, counter, bound, reads) in candidates)
        {
            if (Exits(instructions, indices, head, back).Any(exit => liveness.IsLive(counter, exit))) continue;
            replacements ??= [.. instructions.Select(static instruction => new List<CilInstruction> { instruction })];
            var copyCounter = locals.Count;
            locals.Add(body.Locals[counter]);
            localTypes.Add(body.LocalSignatureTypes[counter]);
            replacements[head - 1] = Versioned(body, head, condition, back, counter, copyCounter, bound, reads, indices, targets);
        }
        return replacements is null
            ? body
            : RenumberedInstructions.Splice(body, replacements) with
            {
                MaxStack = body.MaxStack + TestStack,
                Locals = locals.ToImmutable(),
                LocalSignatureTypes = localTypes.ToImmutable(),
            };
    }

    // In place of the jump into the loop: the tests, the copy's counter set to the loop's, a jump to the copy's
    // condition, the copy, a jump past the loop, and the original jump, where a failed test lands. The copy counts with a
    // counter of its own, so each loop keeps one initialization and one update of its counter, as the array passes after
    // this one require of a counted loop; a counter the loop initializes to a constant just before its jump starts the
    // copy's at that constant too.
    private List<CilInstruction> Versioned(
        CilMethodBody body, int head, int condition, int back, int counter, int copyCounter, CilInstruction[] bound,
        List<Read> reads, Dictionary<int, int> indices, HashSet<int> targets)
    {
        var instructions = body.Instructions;
        var native = body.Locals[counter] == CliValueKind.NativeInt;
        var list = new List<CilInstruction>();
        var failure = new CilOperand.BranchTarget(int.MinValue);
        void Add(CilOperation operation, CilOperand? operand) => list.Add(Instruction(operation, operand ?? new CilOperand.None()));
        void Constant(int value)
        {
            Add(CilOperation.LoadInt32, new CilOperand.ConstantI4(value));
            if (native) Add(CilOperation.ConvertNativeInt, null);
        }
        void Index(int offset)
        {
            Add(CilOperation.LoadLocal, new CilOperand.Index(counter));
            if (offset == 0) return;
            Constant(offset);
            Add(CilOperation.Add, null);
        }

        var firstTest = true;
        foreach (var group in reads.GroupBy(read => (
            Key(instructions[read.Base]),
            Method(instructions[read.Add]).MethodArguments[0].CanonicalName,
            read.Conversion is { } conversion ? instructions[conversion].Operation : CilOperation.Nop,
            Method(instructions[read.Add]).Signature.ParameterTypes[1])))
        {
            var first = group.First();
            var add = instructions[first.Add];
            var element = Method(add).MethodArguments[0];
            var low = group.Min(static read => read.Offset);
            var high = group.Max(static read => read.Offset);
            // i0 + kmin < 0 fails: below it the reads would not be a run from the reference.
            Index(low);
            Constant(0);
            Add(CilOperation.CompareLessThanSigned, null);
            if (!firstTest) Add(CilOperation.BitwiseOr, null);
            firstTest = false;
            // An int index the read sign-extends must stay within int: bound - 1 + kmax > int.MaxValue fails, which an int
            // bound passes for kmax up to 1.
            var zeroExtends = first.Conversion is { } widening && instructions[widening].Operation == CilOperation.ConvertNativeUInt;
            if (!native && high > 1 && !zeroExtends)
            {
                foreach (var instruction in bound) list.Add(Copy(instruction));
                Add(CilOperation.LoadInt32, new CilOperand.ConstantI4(int.MaxValue - high + 1));
                Add(CilOperation.CompareGreaterThanSigned, null);
                Add(CilOperation.BitwiseOr, null);
            }
            NonZeroAddressRange.PushMayHoldZero(
                Add,
                () =>
                {
                    list.Add(Copy(instructions[first.Base]));
                    Index(low);
                    if (first.Conversion is { } conversion) list.Add(Copy(instructions[conversion]));
                    list.Add(Copy(add));
                },
                () =>
                {
                    // (bound - 1 + kmax) - (i0 + kmin), which an int computes exactly modulo 2^32.
                    foreach (var instruction in bound) list.Add(Copy(instruction));
                    Add(CilOperation.LoadLocal, new CilOperand.Index(counter));
                    Add(CilOperation.Subtract, null);
                    Constant(high - low - 1);
                    Add(CilOperation.Add, null);
                },
                element);
            Add(CilOperation.BitwiseOr, null);
        }
        // Every test's failure taken together, so the tests branch once.
        Add(CilOperation.BranchIfTrue, failure);

        if (Initial(instructions, head - 1, counter, targets) is { } initial)
        {
            Add(CilOperation.LoadInt32, new CilOperand.ConstantI4(initial));
        }
        else
        {
            Add(CilOperation.LoadLocal, new CilOperand.Index(counter));
        }
        Add(CilOperation.StoreLocal, new CilOperand.Index(copyCounter));

        // The copy, each read marked; branches within the loop go to the copy, others where they went.
        var marked = reads.Select(static read => read.Add).ToHashSet();
        var positions = new Dictionary<int, int>();
        var copyStart = list.Count + 1;
        var position = copyStart;
        for (var index = head; index <= back; index++)
        {
            positions[index] = position;
            position += marked.Contains(index) ? 2 : 1;
        }
        Add(CilOperation.Branch, RenumberedInstructions.Local(positions[condition]));
        for (var index = head; index <= back; index++)
        {
            var instruction = instructions[index];
            var operand = instruction.Operand switch
            {
                CilOperand.BranchTarget { Offset: var target } when indices[target] is var destination && destination >= head && destination <= back =>
                    RenumberedInstructions.Local(positions[destination]),
                CilOperand.Index { Value: var local } when local == counter &&
                    instruction.Operation is CilOperation.LoadLocal or CilOperation.StoreLocal => new CilOperand.Index(copyCounter),
                var unchanged => unchanged,
            };
            list.Add(new CilInstruction(0, 1, instruction.Operation, operand) { SequencePointOffset = instruction.SequencePointOffset });
            if (marked.Contains(index))
            {
                var element = Method(instruction).MethodArguments[0];
                Add(CilOperation.Call, new CilOperand.MethodInstance(_methods.Resolve(VectorLoopMethod.NonZeroAddress, element)));
            }
        }
        Add(CilOperation.Branch, new CilOperand.BranchTarget(instructions[back].NextOffset));
        var original = list.Count;
        list.Add(instructions[head - 1]);
        for (var index = 0; index < copyStart; index++)
        {
            if (list[index].Operand == failure) list[index] = list[index] with { Operand = RenumberedInstructions.Local(original) };
        }
        return list;
    }

    // The constant the counter holds at the jump into the loop: `ldc.i4 C; stloc i` before it, with C not negative and
    // nothing between them that branches or that a branch reaches; null otherwise.
    private static int? Initial(ImmutableArray<CilInstruction> instructions, int jump, int counter, HashSet<int> targets)
    {
        for (var index = jump - 1; index > 0; index--)
        {
            var instruction = instructions[index];
            if (targets.Contains(instructions[index + 1].Offset) || instruction.Operand is CilOperand.BranchTarget ||
                instruction.Operation is CilOperation.Return or CilOperation.Throw or CilOperation.Rethrow)
            {
                return null;
            }
            if (instruction is { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index { Value: var local } } && local == counter)
            {
                // A store a branch reaches stores what that branch's path pushed, as `i = flag ? 0 : 2` joins there.
                return !targets.Contains(instruction.Offset) &&
                    instructions[index - 1] is { Operation: CilOperation.LoadInt32, Operand: CilOperand.ConstantI4 { Value: >= 0 and var value } }
                        ? value
                        : null;
            }
        }
        return null;
    }

    // Where control goes on leaving the loop: past its back edge, and the targets of its branches out.
    private static List<int> Exits(ImmutableArray<CilInstruction> instructions, Dictionary<int, int> indices, int head, int back)
    {
        var exits = new List<int> { back + 1 };
        for (var index = head; index <= back; index++)
        {
            if (instructions[index].Operand is CilOperand.BranchTarget { Offset: var target } &&
                indices[target] is var destination && (destination < head || destination > back))
            {
                exits.Add(destination);
            }
        }
        return exits;
    }

    // Nothing outside [head, back] branches into it but the jump before its head, and nothing inside branches back short
    // of the head but the loop's own back edge, so the loop is innermost and entered only by the jump to its condition.
    // Only the branches into the range are looked at, found by their target.
    private static bool Enclosed((int Target, int Source)[] branches, int head, int back)
    {
        int low = 0, high = branches.Length;
        while (low < high)
        {
            var middle = (low + high) >>> 1;
            if (branches[middle].Target < head) low = middle + 1;
            else high = middle;
        }
        for (var index = low; index < branches.Length && branches[index].Target <= back; index++)
        {
            var (target, source) = branches[index];
            var inside = source >= head && source <= back;
            if (inside ? target < source && source != back : source != head - 1) return false;
        }
        return true;
    }

    // Constants, and locals and arguments the loop does not write and nothing takes the address of (a write through a
    // reference to one would move the bound past what the test covered), combined by add, subtract and conversions.
    private static bool Invariant(
        CilInstruction[] bound, HashSet<(bool Argument, int Index)> written, HashSet<(bool Argument, int Index)> addressed)
    {
        var depth = 0;
        foreach (var instruction in bound)
        {
            switch (instruction.Operation)
            {
                case CilOperation.Nop:
                    break;
                case CilOperation.LoadInt32:
                    depth++;
                    break;
                case CilOperation.LoadLocal or CilOperation.LoadArgument when Key(instruction) is { } variable:
                    if (written.Contains(variable) || addressed.Contains(variable)) return false;
                    depth++;
                    break;
                case CilOperation.Add or CilOperation.Subtract:
                    if (--depth < 1) return false;
                    break;
                case CilOperation.ConvertInt32 or CilOperation.ConvertNativeInt:
                    if (depth < 1) return false;
                    break;
                default:
                    return false;
            }
        }
        return depth == 1;
    }

    // The index of the load that starts the counter's one update, `i = i + c` with c > 0 just before the condition,
    // which no branch enters part way through; null otherwise.
    private static int? Update(
        CilMethodBody body, int head, int condition, int counter, List<(bool Argument, int Index)> written, HashSet<int> targets)
    {
        var instructions = body.Instructions;
        if (written.Count(store => store == (false, counter)) != 1) return null;
        var store = Previous(instructions, condition, head);
        if (store < head || instructions[store] is not { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index { Value: var local } } ||
            local != counter)
        {
            return null;
        }
        var operation = Previous(instructions, store, head);
        var constant = Previous(instructions, operation, head);
        if (constant >= head && instructions[constant].Operation == CilOperation.ConvertNativeInt &&
            body.Locals[counter] == CliValueKind.NativeInt)
        {
            constant = Previous(instructions, constant, head);
        }
        var load = Previous(instructions, constant, head);
        return load >= head &&
            instructions[operation].Operation == CilOperation.Add &&
            instructions[constant] is { Operation: CilOperation.LoadInt32, Operand: CilOperand.ConstantI4 { Value: > 0 } } &&
            instructions[load] is { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var read } } && read == counter &&
            !Enumerable.Range(load + 1, store - load).Any(next => targets.Contains(instructions[next].Offset))
                ? load
                : null;
    }

    // Unsafe.Add of the counter plus a constant to a reference the loop does not write, before the counter's update,
    // which no branch enters part way through or between the call and what reads its result. The index may also be a
    // copy of the counter plus a constant, as an inlined indexer keeps `values[i + 1]`'s index in its parameter's local:
    // one the loop stores once, before the read on every path to it.
    private List<Read> Reads(
        CilMethodBody body,
        int head,
        int update,
        int counter,
        List<(bool Argument, int Index)> stores,
        HashSet<(bool Argument, int Index)> addressed,
        HashSet<int> targets,
        (int Target, int Source)[] branches)
    {
        var instructions = body.Instructions;
        var written = stores.ToHashSet();
        var kind = body.Locals[counter];
        // Locals stored once in the loop, before the update, as the counter plus a constant: where and what offset.
        var copies = new Dictionary<int, (int Store, int Offset)>();
        foreach (var group in stores.Where(static store => !store.Argument).GroupBy(static store => store.Index))
        {
            if (group.Count() != 1 || group.Key == counter || addressed.Contains((false, group.Key)) || body.Locals[group.Key] != kind) continue;
            for (var store = head; store < update; store++)
            {
                if (instructions[store] is not { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index { Value: var local } } ||
                    local != group.Key)
                {
                    continue;
                }
                if (Index(instructions, Previous(instructions, store, head), head, counter, kind) is var (start, copied) &&
                    !Enumerable.Range(start + 1, store - start).Any(next => targets.Contains(instructions[next].Offset)))
                {
                    copies[local] = (store, copied);
                }
                break;
            }
        }
        var reads = new List<Read>();
        for (var add = head + 2; add < update; add++)
        {
            if (Intrinsic(instructions[add]) != RuntimeIntrinsic.UnsafeAdd ||
                _calledMethods.Resolve(instructions[add]) is not
                {
                    MethodArguments: [_],
                    Signature.ParameterTypes: [CliValueKind.ManagedAddress, var offset and (CliValueKind.I4 or CliValueKind.NativeInt)],
                } ||
                targets.Contains(instructions[add + 1].Offset))
            {
                continue;
            }
            int? conversion = Previous(instructions, add, head) is var before && before >= head &&
                instructions[before].Operation is CilOperation.ConvertNativeInt or CilOperation.ConvertNativeUInt
                ? before
                : null;
            var end = Previous(instructions, conversion ?? add, head);
            var parsed = Index(instructions, end, head, counter, kind);
            if (parsed is null && end >= head &&
                instructions[end] is { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var copy } } &&
                copies.TryGetValue(copy, out var source) && source.Store < end && !Bypassed(branches, source.Store, end))
            {
                parsed = (end, source.Offset);
            }
            if (parsed is not var (index, step) ||
                Previous(instructions, index, head) is var reference && reference < head ||
                (kind, conversion is null, offset) is not ((CliValueKind.I4, true, CliValueKind.I4) or
                    (CliValueKind.I4, false, CliValueKind.NativeInt) or (CliValueKind.NativeInt, true, CliValueKind.NativeInt)) ||
                Key(instructions[reference]) is not { } variable ||
                written.Contains(variable) || addressed.Contains(variable) ||
                !IsReference(body, variable) ||
                Enumerable.Range(reference + 1, add - reference).Any(next => targets.Contains(instructions[next].Offset)))
            {
                continue;
            }
            reads.Add(new(reference, add, conversion, step));
        }
        return reads;
    }

    // Whether a branch from before a copy's store, or the store itself, reaches the code after it up to a read, so the read
    // could see the copy without the store having run in this iteration. The loop is innermost, so a branch from later
    // in it never goes back there.
    private static bool Bypassed((int Target, int Source)[] branches, int store, int read)
    {
        int low = 0, high = branches.Length;
        while (low < high)
        {
            var middle = (low + high) >>> 1;
            if (branches[middle].Target <= store) low = middle + 1;
            else high = middle;
        }
        for (var index = low; index < branches.Length && branches[index].Target <= read; index++)
        {
            if (branches[index].Source <= store) return true;
        }
        return false;
    }

    // The counter, or the counter plus or minus a constant k (as the native int k for a native counter), ending at end:
    // its first instruction and the offset; null otherwise.
    private static (int Start, int Offset)? Index(
        ImmutableArray<CilInstruction> instructions, int end, int head, int counter, CliValueKind kind)
    {
        if (end < head) return null;
        if (instructions[end] is { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var local } })
        {
            return local == counter ? (end, 0) : null;
        }
        if (instructions[end].Operation is not (CilOperation.Add or CilOperation.Subtract)) return null;
        var constant = Previous(instructions, end, head);
        if (kind == CliValueKind.NativeInt && constant >= head && instructions[constant].Operation == CilOperation.ConvertNativeInt)
        {
            constant = Previous(instructions, constant, head);
        }
        var load = Previous(instructions, constant, head);
        return load >= head &&
            instructions[constant] is { Operation: CilOperation.LoadInt32, Operand: CilOperand.ConstantI4 { Value: > int.MinValue and var k } } &&
            instructions[load] is { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var read } } && read == counter
            ? (load, instructions[end].Operation == CilOperation.Add ? k : -k)
            : null;
    }

    // The instruction before index, past any nops, or one before first when there is none.
    private static int Previous(ImmutableArray<CilInstruction> instructions, int index, int first)
    {
        do index--;
        while (index >= first && instructions[index].Operation == CilOperation.Nop);
        return index;
    }

    private static (bool Argument, int Index)? Key(CilInstruction instruction) => instruction switch
    {
        { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var local } } => (false, local),
        { Operation: CilOperation.LoadArgument, Operand: CilOperand.Index { Value: var argument } } => (true, argument),
        _ => null,
    };

    private static bool IsReference(CilMethodBody body, (bool Argument, int Index) variable)
    {
        if (!variable.Argument) return body.Locals[variable.Index] == CliValueKind.ManagedAddress;
        var parameter = body.Method.IsStatic ? variable.Index : variable.Index - 1;
        return parameter >= 0 && body.Method.Signature.ParameterTypes[parameter] == CliValueKind.ManagedAddress;
    }

    private RuntimeIntrinsic? Intrinsic(CilInstruction instruction) =>
        instruction is { Operation: CilOperation.Call, Operand: CilOperand.MethodInstance } &&
        _intrinsics.TryGetIntrinsic(CilOperands.MethodKey(instruction.Operand), out var intrinsic)
            ? intrinsic
            : null;

    private static MethodInstanceModel Method(CilInstruction call) => ((CilOperand.MethodInstance)call.Operand).Value;

    private static CilInstruction Copy(CilInstruction instruction) => Instruction(instruction.Operation, instruction.Operand);

    private static CilInstruction Instruction(CilOperation operation, CilOperand operand) => new(0, 1, operation, operand);
}
