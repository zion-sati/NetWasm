using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Loops;

internal interface IWalkLoopUnroller
{
    CilMethodBody Rewrite(CilMethodBody body);
}

// A walk over n elements, `do { ...; p = ref Unsafe.Add(ref p, 1); n--; } while
// (n != 0)`, pays for advancing p, counting n down and branching once per
// element. Where the vectorizer left it, its body is straight-line code that
// cannot throw and reads p only to load the element or a field of it, and
// nothing else enters its head, a loop ahead of the head runs the body four
// times over, with p advanced 0 to 3 elements, while n, unsigned, holds four,
// then advances p and n by four; it leaves past the walk when n reaches zero,
// and otherwise the walk runs what is left. A walk entered with fewer than four
// elements, zero included, runs as before. Nothing in the body can throw, so no
// handler observes p and n between the iterations a group runs. A fold of the
// body, s = s + e (or |, ^, &) over a 32- or 64-bit integer, chains the four
// elements of a group through s; wrapping integer arithmetic reassociates, so
// the second to fourth fold into their own accumulators, which start at the
// operation's identity and join s when the groups end.
internal sealed class WalkLoopUnroller(ICalledMethodResolver calledMethods, bool enabled) : IWalkLoopUnroller
{
    private const int Factor = 4;

    // A body longer than this stays rolled.
    private const int MaximumBody = 32;

    private readonly ICalledMethodResolver _calledMethods = calledMethods ?? throw new ArgumentNullException(nameof(calledMethods));

    // A fold and the accumulators of the group's second to fourth elements.
    private sealed record Fold(IntegerFold Statement, int[] Partials)
    {
        public int Accumulator => Statement.Accumulator;
    }

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!enabled || !body.Instructions.Any(static instruction => instruction.Operation == CilOperation.BranchIfTrue))
        {
            return body;
        }
        var instructions = body.Instructions;
        List<CilInstruction>[]? replacements = null;
        var partials = new List<CliValueKind>();
        foreach (var walk in CountedWalks.Find(body, _calledMethods))
        {
            // Each walk has a head of its own, which one back edge alone enters.
            if (walk.Body.Length > MaximumBody || !Unrollable(walk))
            {
                continue;
            }
            // The body touches n only in its decrement, which it leaves out, and
            // loads p only to read through it, so no fold accumulates into either.
            var folds = IntegerFolds.Find([.. walk.Body], instructions, body.Locals, static _ => true)
                .Select(fold => new Fold(fold, [.. Enumerable.Range(0, Factor - 1).Select(_ =>
                {
                    partials.Add(fold.Kind);
                    return body.Locals.Length + partials.Count - 1;
                })]))
                .ToArray();
            replacements ??= [.. instructions.Select(static instruction => new List<CilInstruction> { instruction })];
            replacements[walk.Head - 1] = Prefix(instructions[walk.Head - 1], walk, instructions[walk.Back + 1].Offset, folds);
        }
        // Each element reads p advanced by a constant: two more stack slots.
        return replacements is null
            ? body
            : RenumberedInstructions.Splice(body, replacements) with
            {
                MaxStack = body.MaxStack + 2,
                Locals = body.Locals.AddRange(partials),
                LocalSignatureTypes = body.LocalSignatureTypes.AddRange(partials.Select(CliTypeIdentity.FromStackKind)),
            };
    }

    // Whether every instruction is one that cannot throw: locals, constants,
    // arithmetic, bitwise operations, shifts, unchecked conversions and
    // comparisons, and loads through p, which point at an element.
    private static bool Unrollable(CountedWalk walk)
    {
        var body = walk.Body;
        for (var index = 0; index < body.Length; index++)
        {
            var instruction = body[index];
            // What a load of p pushes is used before p moves on, so another
            // instruction follows it.
            if (instruction is { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var local } } &&
                local == walk.Reference)
            {
                if (body[index + 1].Operation is not (CilOperation.LoadObject or CilOperation.LoadField))
                {
                    return false;
                }
                index++;
                continue;
            }
            var safe = instruction.Operation == CilOperation.ConvertNumeric
                ? !((CilOperand.NumericConversion)instruction.Operand).Checked
                : Safe.Contains(instruction.Operation);
            if (!safe) return false;
        }
        return true;
    }

    private static readonly FrozenSet<CilOperation> Safe = new[]
    {
        CilOperation.Nop, CilOperation.Duplicate, CilOperation.Pop, CilOperation.LoadLocal, CilOperation.StoreLocal,
        CilOperation.LoadArgument, CilOperation.LoadInt32, CilOperation.LoadInt64, CilOperation.LoadFloat32,
        CilOperation.LoadFloat64, CilOperation.Add, CilOperation.Subtract, CilOperation.Multiply, CilOperation.BitwiseAnd,
        CilOperation.BitwiseOr, CilOperation.BitwiseXor, CilOperation.ShiftLeft, CilOperation.ShiftRightSigned,
        CilOperation.ShiftRightUnsigned, CilOperation.Negate, CilOperation.OnesComplement, CilOperation.ConvertInt32,
        CilOperation.ConvertInt32Unsigned, CilOperation.ConvertInt64, CilOperation.ConvertInt64Unsigned,
        CilOperation.ConvertNativeInt, CilOperation.ConvertNativeUInt, CilOperation.ConvertFloat32,
        CilOperation.ConvertFloat64, CilOperation.ConvertFloatUnsigned, CilOperation.CompareEqual,
        CilOperation.CompareGreaterThanSigned, CilOperation.CompareGreaterThanUnsigned, CilOperation.CompareLessThanSigned,
        CilOperation.CompareLessThanUnsigned,
    }.ToFrozenSet();

    // The instruction that falls into the walk's head, then the unrolled loop.
    private static List<CilInstruction> Prefix(CilInstruction entry, CountedWalk walk, int exit, Fold[] folds)
    {
        var prefix = new List<CilInstruction> { entry };
        void Add(CilOperation operation, CilOperand? operand = null) =>
            prefix.Add(new CilInstruction(0, 1, operation, operand ?? new CilOperand.None()));
        void Count() => Add(CilOperation.LoadLocal, new CilOperand.Index(walk.Count));
        void Reference(int elements)
        {
            Add(CilOperation.LoadLocal, new CilOperand.Index(walk.Reference));
            if (elements == 0) return;
            Add(CilOperation.LoadInt32, new CilOperand.ConstantI4(elements));
            Add(walk.Advance.Operation, walk.Advance.Operand);
        }

        void Local(CilOperation operation, int local) => Add(operation, new CilOperand.Index(local));

        foreach (var fold in folds)
        {
            foreach (var partial in fold.Partials)
            {
                prefix.Add(fold.Statement.Identity());
                Local(CilOperation.StoreLocal, partial);
            }
        }
        Count();
        Add(CilOperation.LoadInt32, new CilOperand.ConstantI4(Factor));
        var skip = prefix.Count;
        Add(CilOperation.BranchIfLessThanUnsigned);
        var loopStart = prefix.Count;
        for (var element = 0; element < Factor; element++)
        {
            foreach (var instruction in walk.Body)
            {
                if (instruction is { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var local } } &&
                    local == walk.Reference)
                {
                    Reference(element);
                    continue;
                }
                if (element > 0 && instruction is
                    { Operation: CilOperation.LoadLocal or CilOperation.StoreLocal, Operand: CilOperand.Index { Value: var used } } &&
                    folds.FirstOrDefault(fold => fold.Accumulator == used) is { } accumulated)
                {
                    Local(instruction.Operation, accumulated.Partials[element - 1]);
                    continue;
                }
                Add(instruction.Operation, instruction.Operand);
            }
        }
        Reference(Factor);
        Add(CilOperation.StoreLocal, new CilOperand.Index(walk.Reference));
        Count();
        Add(CilOperation.LoadInt32, new CilOperand.ConstantI4(Factor));
        Add(CilOperation.Subtract);
        Add(CilOperation.StoreLocal, new CilOperand.Index(walk.Count));
        Count();
        Add(CilOperation.LoadInt32, new CilOperand.ConstantI4(Factor));
        Add(CilOperation.BranchIfGreaterThanOrEqualUnsigned, RenumberedInstructions.Local(loopStart));
        foreach (var fold in folds)
        {
            Local(CilOperation.LoadLocal, fold.Accumulator);
            foreach (var partial in fold.Partials)
            {
                Local(CilOperation.LoadLocal, partial);
                Add(fold.Statement.Operation);
            }
            Local(CilOperation.StoreLocal, fold.Accumulator);
        }
        // Nothing left: the walk is done.
        Count();
        Add(CilOperation.BranchIfFalse, new CilOperand.BranchTarget(exit));
        prefix[skip] = prefix[skip] with { Operand = RenumberedInstructions.Local(prefix.Count) };
        return prefix;
    }
}
