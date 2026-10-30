using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Loops;

internal interface IIdempotentLoopCollapser
{
    CilMethodBody Rewrite(CilMethodBody body);
}

// A counted loop whose body does the same thing on every iteration runs it
// once. `for (i = K; i < N; i++)` with constants K < N enters its body at least
// once. When the straight-line body reads only what the loop never changes or
// what it wrote earlier in the same iteration, and writes only temporaries and
// blocks (initblk, cpblk, NativeMemory.Fill or NativeMemory.Copy, as
// Unsafe.InitBlock and CopyBlock inline) into locals the loop never reads, each
// later iteration repeats the first. The loop's entry jump and its test then
// go, so the body runs once and falls through to the exit, as LLVM's loop
// deletion leaves it once the repeated writes are hoisted. The counter must be
// unused outside the loop, and cannot step past the bound's range.
internal sealed class IdempotentLoopCollapser(
    IControlFlowGraphBuilder graphs,
    ITypedStackValidator stacks,
    IRuntimeIntrinsicRegistry intrinsics,
    bool enabled) : IIdempotentLoopCollapser
{
    private readonly IControlFlowGraphBuilder _graphs = graphs ?? throw new ArgumentNullException(nameof(graphs));
    private readonly ITypedStackValidator _stacks = stacks ?? throw new ArgumentNullException(nameof(stacks));
    private readonly IRuntimeIntrinsicRegistry _intrinsics = intrinsics ?? throw new ArgumentNullException(nameof(intrinsics));

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!enabled || !body.ExceptionRegions.IsEmpty ||
            !body.Instructions.Any(instruction => Operands(instruction) is not null))
        {
            return body;
        }

        var instructions = body.Instructions;
        var indices = Enumerable.Range(0, instructions.Length).ToDictionary(index => instructions[index].Offset);
        StackSlotProducers? producers = null;
        var rewritten = instructions.ToBuilder();
        var changed = false;
        for (var jump = 2; jump < instructions.Length; jump++)
        {
            if (instructions[jump] is not { Operation: CilOperation.Branch, Operand: CilOperand.BranchTarget { Offset: var test } } ||
                !indices.TryGetValue(test, out var condition) || condition < jump + 6 || condition + 3 >= instructions.Length ||
                instructions[condition] is not { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var counter } } ||
                instructions[condition + 1] is not { Operation: CilOperation.LoadInt32, Operand: CilOperand.ConstantI4 { Value: var limit } } ||
                instructions[condition + 2] is not
                {
                    Operation: CilOperation.BranchIfLessThanSigned,
                    Operand: CilOperand.BranchTarget { Offset: var start },
                } ||
                start != instructions[jump + 1].Offset ||
                instructions[jump - 2] is not { Operation: CilOperation.LoadInt32, Operand: CilOperand.ConstantI4 { Value: var initial } } ||
                instructions[jump - 1] is not { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index { Value: var initialized } } ||
                initialized != counter || initial >= limit)
            {
                continue;
            }
            // The body ends with the counter's increment, which cannot overflow.
            var increment = condition - 4;
            if (instructions[increment] is not { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var read } } || read != counter ||
                instructions[increment + 1] is not { Operation: CilOperation.LoadInt32, Operand: CilOperand.ConstantI4 { Value: > 0 and var step } } ||
                instructions[increment + 2].Operation != CilOperation.Add ||
                instructions[increment + 3] is not { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index { Value: var written } } || written != counter ||
                (long)limit - 1 + step > int.MaxValue)
            {
                continue;
            }
            if (!EntersOnlyThroughTest(instructions, jump, condition) ||
                !CounterIsLocal(instructions, counter, jump - 1, condition, increment) ||
                !IsIdempotent(body, jump + 1, increment, producers ??= Producers(body)))
            {
                continue;
            }

            foreach (var index in new[] { jump, condition, condition + 1, condition + 2 })
            {
                rewritten[index] = instructions[index] with { Operation = CilOperation.Nop, Operand = new CilOperand.None() };
            }
            changed = true;
        }
        return changed ? body with { Instructions = rewritten.ToImmutable() } : body;
    }

    private StackSlotProducers Producers(CilMethodBody body) =>
        new(body, _stacks.Validate(_graphs.Build(body)).InstructionEntryStacks);

    // Control reaches the counter's store, the jump and the loop only by
    // falling into the store, and only the test branches back. The body's own
    // instructions cannot branch: IsIdempotent admits none.
    private static bool EntersOnlyThroughTest(IReadOnlyList<CilInstruction> instructions, int jump, int condition)
    {
        var first = instructions[jump - 1].Offset;
        var last = instructions[condition + 2].Offset;
        for (var index = 0; index < instructions.Count; index++)
        {
            if (index == jump || index == condition + 2) continue;
            var targets = instructions[index].Operand switch
            {
                CilOperand.BranchTarget target => [target.Offset],
                CilOperand.SwitchTargets cases => cases.Offsets,
                _ => [],
            };
            if (targets.Any(target => target >= first && target <= last)) return false;
        }
        return true;
    }

    // The counter is set before the jump, then read and written only by the
    // test and the increment.
    private static bool CounterIsLocal(IReadOnlyList<CilInstruction> instructions, int counter, int initialization, int condition, int increment)
    {
        for (var index = 0; index < instructions.Count; index++)
        {
            if (instructions[index] is not
                {
                    Operation: CilOperation.LoadLocal or CilOperation.LoadLocalAddress or CilOperation.StoreLocal,
                    Operand: CilOperand.Index { Value: var local },
                } || local != counter)
            {
                continue;
            }
            if (index != initialization && index != condition && index != increment && index != increment + 3) return false;
        }
        return true;
    }

    // Every iteration repeats the first: each local the body reads is one it
    // never writes or wrote earlier in this iteration, nothing reads a block it
    // writes, no copy reads a block the loop writes, and nothing else has an effect.
    private bool IsIdempotent(CilMethodBody body, int first, int end, StackSlotProducers producers)
    {
        var instructions = body.Instructions;
        var stored = new HashSet<int>();
        for (var index = first; index < end; index++)
        {
            if (instructions[index] is { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index { Value: var local } }) stored.Add(local);
        }
        var defined = new HashSet<int>();
        var destinations = new HashSet<int>();
        var sources = new HashSet<int>();
        var traced = new HashSet<int>();
        for (var index = first; index < end; index++)
        {
            var instruction = instructions[index];
            switch (instruction.Operation)
            {
                case CilOperation.Nop or CilOperation.LoadInt32 or CilOperation.LoadInt64 or CilOperation.LoadFloat32 or
                    CilOperation.LoadFloat64 or CilOperation.LoadNull or CilOperation.LoadArgument or
                    CilOperation.ConvertNativeInt or CilOperation.ConvertNativeUInt or CilOperation.Add or
                    CilOperation.Subtract or CilOperation.Multiply or CilOperation.BitwiseAnd or CilOperation.BitwiseOr or
                    CilOperation.BitwiseXor or CilOperation.ShiftLeft or CilOperation.ShiftRightSigned or
                    CilOperation.ShiftRightUnsigned or CilOperation.Negate or CilOperation.OnesComplement or
                    CilOperation.Pop or CilOperation.Unaligned or CilOperation.LoadLocalAddress:
                    break;
                case CilOperation.ConvertNumeric when instruction.Operand is CilOperand.NumericConversion { Checked: false }:
                    break;
                case CilOperation.LoadLocal when instruction.Operand is CilOperand.Index { Value: var local }:
                    if (stored.Contains(local) && !defined.Contains(local)) return false;
                    break;
                case CilOperation.StoreLocal when instruction.Operand is CilOperand.Index { Value: var local }:
                    defined.Add(local);
                    break;
                default:
                    if (Operands(instruction) is not { } operands ||
                        !producers.Stacks.TryGetValue(instruction.Offset, out var stack) ||
                        Root(index, stack.Length - 3 + operands.Destination, first, producers, instructions, traced) is not { } destination)
                    {
                        return false;
                    }
                    destinations.Add(destination);
                    if (operands.Source is { } source)
                    {
                        if (Root(index, stack.Length - 3 + source, first, producers, instructions, traced) is not { } read) return false;
                        sources.Add(read);
                    }
                    break;
            }
        }
        // Every address the body takes is a block write's destination or source.
        for (var index = first; index < end; index++)
        {
            if (instructions[index] is { Operation: CilOperation.LoadLocalAddress, Operand: CilOperand.Index { Value: var local } } &&
                (!traced.Contains(index) || stored.Contains(local)))
            {
                return false;
            }
        }
        if (destinations.Overlaps(sources)) return false;
        // A written block is not read in the loop as a value either.
        for (var index = first; index < end; index++)
        {
            if (instructions[index] is { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var local } } &&
                destinations.Contains(local))
            {
                return false;
            }
        }
        return destinations.All(local => (uint)local < (uint)body.LocalSignatureTypes.Length &&
            body.LocalSignatureTypes[local].IsValueType);
    }

    // The local whose address an operand carries: an ldloca behind native-int
    // conversions and constant offsets, directly or through a temporary stored
    // earlier in the body.
    private static int? Root(int consumer, int slot, int first, StackSlotProducers producers,
        IReadOnlyList<CilInstruction> instructions, HashSet<int> traced)
    {
        if (producers.Find(consumer, slot) is not { } index) return null;
        while (true)
        {
            switch (instructions[index])
            {
                case { Operation: CilOperation.ConvertNativeUInt or CilOperation.ConvertNativeInt }:
                    if (producers.FindTop(index) is not { } source) return null;
                    index = source;
                    continue;
                case { Operation: CilOperation.Add }:
                    if (!producers.Stacks.TryGetValue(instructions[index].Offset, out var stack) ||
                        producers.Find(index, stack.Length - 2) is not { } left || producers.FindTop(index) is not { } right)
                    {
                        return null;
                    }
                    index = IsConstant(right, producers, instructions) ? left
                        : IsConstant(left, producers, instructions) ? right
                        : -1;
                    if (index < 0) return null;
                    continue;
                case { Operation: CilOperation.LoadLocalAddress, Operand: CilOperand.Index { Value: var local } }:
                    traced.Add(index);
                    return local;
                case { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var temporary } }:
                    var store = -1;
                    for (var at = index - 1; at >= first; at--)
                    {
                        if (instructions[at] is { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index { Value: var target } } &&
                            target == temporary)
                        {
                            store = at;
                            break;
                        }
                    }
                    if (store < 0 || producers.FindTop(store) is not { } value) return null;
                    index = value;
                    continue;
                default:
                    return null;
            }
        }
    }

    // A constant offset, behind numeric conversions.
    private static bool IsConstant(int index, StackSlotProducers producers, IReadOnlyList<CilInstruction> instructions)
    {
        while (instructions[index].Operation is
               CilOperation.ConvertNativeInt or CilOperation.ConvertNativeUInt or CilOperation.ConvertNumeric)
        {
            if (producers.FindTop(index) is not { } source) return false;
            index = source;
        }
        return instructions[index].Operation is CilOperation.LoadInt32 or CilOperation.LoadInt64;
    }

    private sealed record BlockOperands(int Destination, int? Source);

    // The positions, among a block write's three operands, of its destination
    // and its source.
    private BlockOperands? Operands(CilInstruction instruction) => instruction.Operation switch
    {
        CilOperation.InitializeBlock => new(0, null),
        CilOperation.CopyBlock => new(0, 1),
        CilOperation.Call when Intrinsic(instruction) == RuntimeIntrinsic.NativeMemoryFill => new(0, null),
        // NativeMemory.Copy(source, destination, byteCount)
        CilOperation.Call when Intrinsic(instruction) == RuntimeIntrinsic.NativeMemoryCopy => new(1, 0),
        _ => null,
    };

    private RuntimeIntrinsic? Intrinsic(CilInstruction instruction)
    {
        var key = instruction.Operand switch
        {
            CilOperand.Entity entity => entity.Key,
            CilOperand.MethodInstance instance => instance.Value.Definition.Key,
            _ => (EntityKey?)null,
        };
        return key is { } method && _intrinsics.TryGetIntrinsic(method, out var intrinsic) ? intrinsic : null;
    }
}
