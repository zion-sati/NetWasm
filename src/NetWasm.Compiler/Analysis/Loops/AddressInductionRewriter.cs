using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Loops;

internal interface IAddressInductionRewriter
{
    CilMethodBody Rewrite(CilMethodBody body);
}

// Upstream's vector loops read `Unsafe.Add(ref first, (nint)i)` with `i += 8`,
// as do span indexers inlined into a counted loop, so every load waits on the
// counter, its scaling and an add (SequenceCompareTo's char loop: 350 ns for
// 3,000 chars against 225 with references that advance on their own). In an
// innermost loop whose counter i, a local of int or native int, it writes only
// as `i = i + c` or `i - c` with a constant c, each Unsafe.Add of i to a
// reference the loop does not write, a local or argument nothing takes the
// address of, gets a reference local p: the loop's entry sets p to that
// address, its reads of it read p (and of `i + k`, Unsafe.Add of p and k), and
// p moves c elements just before i moves c, so p equals the address everywhere
// the loop reads it. Elements wider than a byte save the counter's scaling; a
// byte's address costs the add p's advance does, so byte reads stay. An int counter
// passed as a native int sign-extends into p; a read that zero-extends it
// (upstream's span indexer passes `(nint)(uint)index`) reads the same address
// wherever the address lies within its object, where a negative int would
// lie 4 GiB on. The loop is entered at its head, which the instruction before
// falls into, or at its condition, which a jump just before the head reaches and
// other jumps from outside then reach through it; it allocates nothing and calls
// nothing but Unsafe and Vector128 intrinsics, except on its way to a throw,
// so no collection can see p, which may end up past its object's end; methods
// with protected regions are left alone.
internal sealed class AddressInductionRewriter(
    ICalledMethodResolver calledMethods,
    IRuntimeIntrinsicRegistry intrinsics,
    bool enabled) : IAddressInductionRewriter
{
    // Intrinsics that compute addresses and sizes, or Vector128 values, and allocate nothing.
    private static readonly ImmutableHashSet<RuntimeIntrinsic> AddressIntrinsics =
    [
        RuntimeIntrinsic.UnsafeAdd, RuntimeIntrinsic.UnsafeAs, RuntimeIntrinsic.UnsafeAsRef,
        RuntimeIntrinsic.UnsafeAsRefManaged, RuntimeIntrinsic.UnsafeNullRef, RuntimeIntrinsic.UnsafeIsAddressGreaterThan,
        RuntimeIntrinsic.UnsafeSizeOf, RuntimeIntrinsic.UnsafeByteOffset, RuntimeIntrinsic.UnsafeAddByteOffset,
    ];

    // Instructions that call code or allocate, either of which may collect.
    private static readonly ImmutableHashSet<CilOperation> Collecting =
    [
        CilOperation.Call, CilOperation.CallVirtual, CilOperation.CallIndirect, CilOperation.NewObject, CilOperation.Box,
        CilOperation.NewArray, CilOperation.NewRectangularArray, CilOperation.NewBoundedRectangularArray,
        CilOperation.LoadString, CilOperation.MaterializeType, CilOperation.MaterializeMethod, CilOperation.MaterializeField,
        CilOperation.GetObjectType, CilOperation.LoadFunction, CilOperation.LoadVirtualFunction,
    ];

    private readonly ICalledMethodResolver _calledMethods = calledMethods ?? throw new ArgumentNullException(nameof(calledMethods));
    private readonly IRuntimeIntrinsicRegistry _intrinsics = intrinsics ?? throw new ArgumentNullException(nameof(intrinsics));

    // A read of base plus the counter and Offset: Base loads the reference and
    // Add is the call; the instructions between compute the index.
    private sealed record Read(int Base, int Add, int Offset);

    // The counter's update: Local is the counter, Load starts the update and Step is c.
    private sealed record Increment(int Local, int Load, int Step);

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var instructions = body.Instructions;
        if (!enabled || !body.ExceptionRegions.IsEmpty ||
            !instructions.Any(instruction => Intrinsic(instruction) == RuntimeIntrinsic.UnsafeAdd))
        {
            return body;
        }
        var indices = Enumerable.Range(0, instructions.Length).ToDictionary(index => instructions[index].Offset);
        var targets = instructions.SelectMany(static instruction => Destinations(instruction)).ToHashSet();
        var addressed = instructions
            .Where(static instruction => instruction.Operation is CilOperation.LoadLocalAddress or CilOperation.LoadArgumentAddress)
            .Select(static instruction => (instruction.Operation == CilOperation.LoadArgumentAddress, ((CilOperand.Index)instruction.Operand).Value))
            .ToHashSet();
        var locals = body.Locals.ToBuilder();
        var localTypes = body.LocalSignatureTypes.ToBuilder();
        List<CilInstruction>[]? replacements = null;
        // The instructions earlier loops' rewrites replace.
        var claimed = new HashSet<int>();
        for (var back = 1; back < instructions.Length; back++)
        {
            if (instructions[back] is not { Operand: CilOperand.BranchTarget { Offset: var headOffset } } candidate ||
                !IsConditionalBranch(candidate) ||
                indices[headOffset] is not (> 0 and var head) || head >= back ||
                Entry(instructions, indices, head, back) is not { } entry ||
                Entries(instructions, indices, head, back, entry) is not { } redirects ||
                !Quiet(instructions, head, back))
            {
                continue;
            }
            foreach (var increment in Increments(body, head, back, addressed, targets))
            {
                var reads = Reads(body, head, back, increment.Local, addressed, targets);
                if (reads.Count == 0) continue;
                // A jump to an earlier loop's condition can be the instruction that falls into this one.
                int[] replaced =
                [
                    .. reads.SelectMany(static read => Enumerable.Range(read.Base, read.Add - read.Base + 1)),
                    head - 1, increment.Load, .. redirects,
                ];
                if (replaced.Any(claimed.Contains)) break;
                claimed.UnionWith(replaced);
                replacements ??= [.. instructions.Select(static instruction => new List<CilInstruction> { instruction })];
                var prologue = new List<CilInstruction>();
                var advance = new List<CilInstruction>();
                // One reference per base, element type and offset kind.
                foreach (var group in reads.GroupBy(read => (
                    Key(instructions[read.Base]),
                    Method(instructions[read.Add]).MethodArguments[0],
                    Method(instructions[read.Add]).Signature.ParameterTypes[1])))
                {
                    var first = group.First();
                    var add = instructions[first.Add];
                    var method = Method(add);
                    var native = method.Signature.ParameterTypes[1] == CliValueKind.NativeInt;
                    var widen = native && body.Locals[increment.Local] == CliValueKind.I4;
                    var type = CliTypeIdentity.ManagedByReference(method.MethodArguments[0]);
                    locals.Add(type.StackKind);
                    localTypes.Add(type);
                    var reference = locals.Count - 1;
                    prologue.Add(Copy(instructions[first.Base]));
                    prologue.Add(Instruction(CilOperation.LoadLocal, new CilOperand.Index(increment.Local)));
                    if (widen) prologue.Add(Instruction(CilOperation.ConvertNativeInt, new CilOperand.None()));
                    prologue.Add(Copy(add));
                    prologue.Add(Instruction(CilOperation.StoreLocal, new CilOperand.Index(reference)));
                    advance.Add(Instruction(CilOperation.LoadLocal, new CilOperand.Index(reference)));
                    advance.Add(Instruction(CilOperation.LoadInt32, new CilOperand.ConstantI4(increment.Step)));
                    if (native) advance.Add(Instruction(CilOperation.ConvertNativeInt, new CilOperand.None()));
                    advance.Add(Copy(add));
                    advance.Add(Instruction(CilOperation.StoreLocal, new CilOperand.Index(reference)));
                    foreach (var read in group)
                    {
                        replacements[read.Base] = [Instruction(CilOperation.LoadLocal, new CilOperand.Index(reference))];
                        for (var index = read.Base + 1; index < read.Add; index++) replacements[index] = [];
                        // Unsafe.Add(ref base, i + k) is Unsafe.Add(ref p, k).
                        replacements[read.Add] = read.Offset == 0
                            ? []
                            : native
                                ? [Instruction(CilOperation.LoadInt32, new CilOperand.ConstantI4(read.Offset)),
                                    Instruction(CilOperation.ConvertNativeInt, new CilOperand.None()), Copy(add)]
                                : [Instruction(CilOperation.LoadInt32, new CilOperand.ConstantI4(read.Offset)), Copy(add)];
                    }
                }
                // The jump to the condition, or the instruction that falls into the head,
                // sets the references just before the loop runs.
                replacements[head - 1] = entry == head
                    ? [instructions[head - 1], .. prologue]
                    : [.. prologue, instructions[head - 1]];
                // p moves just before i, so a loop that ends with i's update still does, as the
                // array unroller expects, and a jump to the update moves both.
                replacements[increment.Load] = [.. advance, instructions[increment.Load]];
                // Other jumps to the condition go to the one before the head, through the prologue.
                foreach (var redirect in redirects)
                {
                    replacements[redirect] =
                        [Instruction(instructions[redirect].Operation, new CilOperand.BranchTarget(instructions[head - 1].Offset))];
                }
                break;
            }
        }
        return replacements is null
            ? body
            : RenumberedInstructions.Splice(body, replacements) with
            {
                MaxStack = body.MaxStack + 2,
                Locals = locals.ToImmutable(),
                LocalSignatureTypes = localTypes.ToImmutable(),
            };
    }

    // Where the loop is entered: its condition, which an unconditional jump just
    // before the head reaches, or else its head; null for a jump elsewhere. A head
    // the instruction before cannot fall into, and nothing outside branches to,
    // is never reached, so its rewrite changes nothing.
    private static int? Entry(ImmutableArray<CilInstruction> instructions, Dictionary<int, int> indices, int head, int back) =>
        instructions[head - 1] is { Operation: CilOperation.Branch, Operand: CilOperand.BranchTarget { Offset: var condition } }
            ? indices[condition] is var index && index > head && index <= back ? index : null
            : head;

    // The branches outside the loop, other than the jump before its head, that
    // enter it at its condition, when nothing else outside branches into it
    // and nothing inside it branches back short of the head, so the loop is
    // innermost; null otherwise. Nothing outside may branch into a loop entered
    // at its head, which the prologue after the instruction before it would miss.
    private static List<int>? Entries(
        ImmutableArray<CilInstruction> instructions, Dictionary<int, int> indices, int head, int back, int entry)
    {
        var redirects = new List<int>();
        for (var index = 0; index < instructions.Length; index++)
        {
            var inside = index >= head && index <= back;
            foreach (var destination in Destinations(instructions[index]))
            {
                var target = indices[destination];
                if (inside ? target < index && target >= head && (target != head || index != back) : target >= head && target <= back)
                {
                    if (inside || entry == head || target != entry || instructions[index].Operand is not CilOperand.BranchTarget)
                    {
                        return null;
                    }
                    if (index != head - 1) redirects.Add(index);
                }
            }
        }
        return redirects;
    }

    // No instruction in the loop can start a collection, except on its way to a throw.
    private bool Quiet(ImmutableArray<CilInstruction> instructions, int head, int back)
    {
        for (var index = head; index < back; index++)
        {
            var instruction = instructions[index];
            if (Collecting.Contains(instruction.Operation) &&
                !(Intrinsic(instruction) is { } intrinsic &&
                    (AddressIntrinsics.Contains(intrinsic) || RuntimeIntrinsicKinds.IsSimdVector(intrinsic))) &&
                !Throws(instructions, index + 1, back))
            {
                return false;
            }
        }
        return true;
    }

    // A throw follows, after a null at most.
    private static bool Throws(ImmutableArray<CilInstruction> instructions, int index, int back)
    {
        if (index < back && instructions[index].Operation == CilOperation.LoadNull) index++;
        return index < back && instructions[index].Operation == CilOperation.Throw;
    }

    // Counters the loop writes once, as `i = i + c` or `i - c` with a constant c
    // that no branch enters after the counter's load.
    private static IEnumerable<Increment> Increments(
        CilMethodBody body, int head, int back, HashSet<(bool Argument, int Index)> addressed, HashSet<int> targets)
    {
        var instructions = body.Instructions;
        var writes = Enumerable.Range(head, back - head)
            .Where(index => instructions[index].Operation == CilOperation.StoreLocal)
            .GroupBy(index => ((CilOperand.Index)instructions[index].Operand).Value);
        foreach (var write in writes)
        {
            var local = write.Key;
            if (write.Count() != 1 || addressed.Contains((false, local)) ||
                body.Locals[local] is not (CliValueKind.I4 or CliValueKind.NativeInt))
            {
                continue;
            }
            var store = write.Single();
            var operation = Previous(instructions, store, head);
            var constant = Previous(instructions, operation, head);
            if (constant >= head && instructions[constant].Operation is CilOperation.ConvertNativeInt or CilOperation.ConvertNativeUInt &&
                body.Locals[local] == CliValueKind.NativeInt)
            {
                constant = Previous(instructions, constant, head);
            }
            var load = Previous(instructions, constant, head);
            if (load >= head &&
                instructions[operation].Operation is CilOperation.Add or CilOperation.Subtract &&
                instructions[constant] is { Operation: CilOperation.LoadInt32, Operand: CilOperand.ConstantI4 { Value: var step } } &&
                step is > int.MinValue and not 0 &&
                instructions[load] is { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var read } } &&
                read == local &&
                !Enumerable.Range(load + 1, store - load).Any(next => targets.Contains(instructions[next].Offset)))
            {
                yield return new(local, load, instructions[operation].Operation == CilOperation.Add ? step : -step);
            }
        }
    }

    // Unsafe.Add of the counter to a reference the loop does not write, which
    // no branch enters part way through.
    private List<Read> Reads(
        CilMethodBody body,
        int head,
        int back,
        int counter,
        HashSet<(bool Argument, int Index)> addressed,
        HashSet<int> targets)
    {
        var instructions = body.Instructions;
        var written = Enumerable.Range(head, back - head)
            .Where(index => instructions[index].Operation is CilOperation.StoreLocal or CilOperation.StoreArgument)
            .Select(index => (instructions[index].Operation == CilOperation.StoreArgument, ((CilOperand.Index)instructions[index].Operand).Value))
            .ToHashSet();
        var reads = new List<Read>();
        for (var add = head + 2; add < back; add++)
        {
            if (Intrinsic(instructions[add]) != RuntimeIntrinsic.UnsafeAdd ||
                _calledMethods.Resolve(instructions[add]) is not
                {
                    MethodArguments: [var element],
                    Signature.ParameterTypes: [CliValueKind.ManagedAddress, var offset and (CliValueKind.I4 or CliValueKind.NativeInt)],
                } ||
                // A byte's address is the reference plus the counter, one add as p's advance is; the counter
                // stays live, so p would only add a register (Span.IndexerBench.Indexer5 1.07).
                PrimitiveElements.Size(element) == 1)
            {
                continue;
            }
            int? conversion = Previous(instructions, add, head) is var before && before >= head &&
                instructions[before].Operation is CilOperation.ConvertNativeInt or CilOperation.ConvertNativeUInt
                ? before
                : null;
            var kind = body.Locals[counter];
            if (Index(instructions, Previous(instructions, conversion ?? add, head), head, counter, kind) is not var (index, step) ||
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
            reads.Add(new(reference, add, step));
        }
        return reads;
    }

    // The counter, or the counter plus a constant k (as the native int k for a
    // native counter), ending at end: its first instruction and k; null otherwise.
    private static (int Start, int Offset)? Index(
        ImmutableArray<CilInstruction> instructions, int end, int head, int counter, CliValueKind kind)
    {
        if (end < head) return null;
        if (instructions[end] is { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var local } })
        {
            return local == counter ? (end, 0) : null;
        }
        if (instructions[end].Operation != CilOperation.Add) return null;
        var constant = Previous(instructions, end, head);
        if (kind == CliValueKind.NativeInt && constant >= head &&
            instructions[constant].Operation is CilOperation.ConvertNativeInt or CilOperation.ConvertNativeUInt)
        {
            constant = Previous(instructions, constant, head);
        }
        var load = Previous(instructions, constant, head);
        return load >= head &&
            instructions[constant] is { Operation: CilOperation.LoadInt32, Operand: CilOperand.ConstantI4 { Value: var k } } &&
            instructions[load] is { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var read } } && read == counter
            ? (load, k)
            : null;
    }

    // The instruction before index, past any nops, or one before first when there is none.
    private static int Previous(ImmutableArray<CilInstruction> instructions, int index, int first)
    {
        do index--;
        while (index >= first && instructions[index].Operation == CilOperation.Nop);
        return index;
    }

    private static ImmutableArray<int> Destinations(CilInstruction instruction) => instruction.Operand switch
    {
        CilOperand.BranchTarget target => [target.Offset],
        CilOperand.SwitchTargets switchTargets => switchTargets.Offsets,
        _ => [],
    };

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

    // brtrue to ble.un, which CilOperation lists in a row.
    private static bool IsConditionalBranch(CilInstruction instruction) =>
        instruction.Operation is >= CilOperation.BranchIfTrue and <= CilOperation.BranchIfLessThanOrEqualUnsigned;

    private RuntimeIntrinsic? Intrinsic(CilInstruction instruction) =>
        instruction is { Operation: CilOperation.Call, Operand: CilOperand.MethodInstance } &&
        _intrinsics.TryGetIntrinsic(CilOperands.MethodKey(instruction.Operand), out var intrinsic)
            ? intrinsic
            : null;

    private static MethodInstanceModel Method(CilInstruction call) => ((CilOperand.MethodInstance)call.Operand).Value;

    private static CilInstruction Copy(CilInstruction instruction) => Instruction(instruction.Operation, instruction.Operand);

    private static CilInstruction Instruction(CilOperation operation, CilOperand operand) => new(0, 1, operation, operand);
}
