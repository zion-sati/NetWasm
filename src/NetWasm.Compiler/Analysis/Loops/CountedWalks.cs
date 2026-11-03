using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Loops;

// A do-while that walks Reference over Count elements: Roslyn's
// `do { ...; p = ref Unsafe.Add(ref p, 1); n--; } while (n != 0)`. Head and Back
// index its first instruction and its `brtrue`; Body is what it does per
// element, without the decrement; Advance is the Unsafe.Add call that moves p
// one Element on.
internal sealed record CountedWalk(
    int Head,
    int Back,
    int Reference,
    int Count,
    CilInstruction Advance,
    CliTypeIdentity Element,
    CilInstruction[] Body);

internal static class CountedWalks
{
    // The walks whose head nothing enters but the back edge and the
    // instruction before it, which falls through: a prefix after that
    // instruction runs once ahead of the walk.
    public static IEnumerable<CountedWalk> Find(CilMethodBody body, ICalledMethodResolver calledMethods)
    {
        var instructions = body.Instructions;
        var indices = Enumerable.Range(0, instructions.Length).ToDictionary(index => instructions[index].Offset);
        var branches = instructions
            .Select(static instruction => instruction.Operand)
            .OfType<CilOperand.BranchTarget>()
            .GroupBy(static target => target.Offset)
            .ToDictionary(static group => group.Key, static group => group.Count());
        for (var back = 1; back < instructions.Length - 1; back++)
        {
            if (instructions[back] is { Operation: CilOperation.BranchIfTrue, Operand: CilOperand.BranchTarget { Offset: var head } } &&
                branches[head] == 1 &&
                indices.TryGetValue(head, out var start) && start >= 1 && start < back &&
                FallsThrough(instructions[start - 1]) &&
                Match(instructions, start, back, body.Locals, calledMethods) is { } walk)
            {
                yield return walk;
            }
        }
    }

    private static bool FallsThrough(CilInstruction instruction) =>
        instruction.Operand is not CilOperand.BranchTarget &&
        instruction.Operation is not (CilOperation.Return or CilOperation.Throw or CilOperation.Rethrow or
            CilOperation.EndFinally or CilOperation.EndFilter);

    // The do-while at [start, back]: a body that ends p = ref Unsafe.Add(ref p, 1),
    // after its reads of p, and holds n = n - 1 once anywhere, then
    // `ldloc n; brtrue start`. The body branches nowhere, never addresses a local
    // or an argument, and touches n and writes p nowhere else; its nops do
    // nothing and are left out.
    private static CountedWalk? Match(
        ImmutableArray<CilInstruction> instructions,
        int start,
        int back,
        ImmutableArray<CliValueKind> locals,
        ICalledMethodResolver calledMethods)
    {
        if (back - start < 10 ||
            instructions[back - 1] is not { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var count } } ||
            (uint)count >= (uint)locals.Length || locals[count] != CliValueKind.I4)
        {
            return null;
        }
        bool IsDecrement(int at) => instructions.AsSpan(at, 4) is [
        { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var read } },
        { Operation: CilOperation.LoadInt32, Operand: CilOperand.ConstantI4 { Value: 1 } },
        { Operation: CilOperation.Subtract },
        { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index { Value: var decrement } },
            ] && read == count && decrement == count;
        (int Reference, CilInstruction Advance, CliTypeIdentity Element)? Advance(int at) =>
            instructions.AsSpan(at, 4) is [
            { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var from } },
            { Operation: CilOperation.LoadInt32, Operand: CilOperand.ConstantI4 { Value: 1 } },
            { Operation: CilOperation.Call } call,
            { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index { Value: var to } },
            ] && from == to &&
            calledMethods.Resolve(call) is
            {
                DeclaringType.FullName: "System.Runtime.CompilerServices.Unsafe",
                Definition.Name: "Add",
                MethodArguments: [var type],
                Signature.ParameterSignatureTypes: [_, { CanonicalName: "primitive:i4" }],
            }
                ? (from, call, type)
                : null;

        // The advance last, or the advance then the decrement.
        var advanceAt = Advance(back - 5) is not null ? back - 5 : IsDecrement(back - 5) ? back - 9 : -1;
        if (advanceAt < start || Advance(advanceAt) is not var (reference, advance, element))
        {
            return null;
        }
        var statements = instructions.AsSpan(start, advanceAt - start).ToArray().ToList();
        if (advanceAt == back - 5)
        {
            var decrements = Enumerable.Range(start, Math.Max(0, advanceAt - start - 3)).Where(IsDecrement).ToArray();
            if (decrements is not [var at]) return null;
            statements.RemoveRange(at - start, 4);
        }
        // Passes that remove instructions leave nops, as an element copied
        // into a local and read a field at a time does between p and the read.
        statements.RemoveAll(static instruction => instruction.Operation == CilOperation.Nop);
        if (statements.Any(instruction => instruction.Operand is CilOperand.BranchTarget ||
                instruction.Operation is CilOperation.StoreArgument or CilOperation.LoadLocalAddress or
                    CilOperation.LoadArgumentAddress ||
                instruction.Operand is CilOperand.Index { Value: var local } && local == count &&
                    instruction.Operation is CilOperation.LoadLocal or CilOperation.StoreLocal ||
                instruction is { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index { Value: var stored } } &&
                    stored == reference))
        {
            return null;
        }
        return new CountedWalk(start, back, reference, count, advance, element, [.. statements]);
    }
}
