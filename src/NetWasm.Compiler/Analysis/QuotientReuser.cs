using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
// A local, an argument or a constant, by the instruction that pushes it.
using Operand = (NetWasm.Compiler.Core.CilOperation Operation, long Value);

namespace NetWasm.Compiler.Analysis;

internal interface IQuotientReuser
{
    CilMethodBody Rewrite(CilMethodBody body);
}

// A remainder of two values an earlier division divided takes the quotient
// that division found: `value % divisor` after `value / divisor` is
// `value - quotient * divisor`, a multiply where it was a second division, as
// LLVM's DivRemPairs computes it. The division returned, so the divisor is
// not zero and the pair is not MinValue and -1, and the remainder, which
// throws for the same pairs, cannot throw. Each operand is a local, an
// argument or a constant pushed just before the operation, and both are
// integers of one size; a signed remainder takes a signed quotient and an
// unsigned one an unsigned quotient. The quotient is available where every
// path to the remainder divides after the last store to either operand.
// Slots whose address the method takes, or that a handler stores, are left
// alone, as a finally a leave runs could change them on the way to its
// target, and a handler starts with nothing available. Every division whose
// quotient a remainder reuses keeps it in a local.
internal sealed class QuotientReuser(
    IControlFlowGraphBuilder graphs,
    ITypedStackValidator stacks,
    bool enabled) : IQuotientReuser
{
    private readonly IControlFlowGraphBuilder _graphs = graphs ?? throw new ArgumentNullException(nameof(graphs));
    private readonly ITypedStackValidator _stacks = stacks ?? throw new ArgumentNullException(nameof(stacks));

    private readonly record struct Key(Operand Dividend, Operand Divisor, bool Unsigned);

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var instructions = body.Instructions;
        if (!enabled ||
            !instructions.Any(static instruction => instruction.Operation is CilOperation.Remainder or CilOperation.RemainderUnsigned))
        {
            return body;
        }
        var controlFlow = _stacks.Validate(_graphs.Build(body));
        var graph = controlFlow.Graph;
        var starts = graph.Blocks.Select(static block => block.StartOffset).ToHashSet();
        var excluded = Excluded(body);
        // The divisions and remainders that can take part: the instruction index of the operation, with its key and type.
        var divisions = new Dictionary<int, (Key Key, CliValueKind Kind)>();
        var remainders = new Dictionary<int, Key>();
        for (var index = 2; index < instructions.Length; index++)
        {
            var instruction = instructions[index];
            var unsigned = instruction.Operation is CilOperation.DivideUnsigned or CilOperation.RemainderUnsigned;
            if (instruction.Operation is not (CilOperation.Divide or CilOperation.DivideUnsigned or
                    CilOperation.Remainder or CilOperation.RemainderUnsigned) ||
                starts.Contains(instruction.Offset) || starts.Contains(instructions[index - 1].Offset) ||
                OperandOf(instructions[index - 2]) is not { } dividend || OperandOf(instructions[index - 1]) is not { } divisor ||
                excluded.Contains(dividend) || excluded.Contains(divisor) ||
                !controlFlow.InstructionEntryStacks.TryGetValue(instruction.Offset, out var stack) ||
                stack[^1] != stack[^2] || stack[^1] is not (CliValueKind.I4 or CliValueKind.I8 or CliValueKind.NativeInt))
            {
                continue;
            }
            var key = new Key(dividend, divisor, unsigned);
            if (instruction.Operation is CilOperation.Divide or CilOperation.DivideUnsigned) divisions[index] = (key, stack[^1]);
            else remainders[index] = key;
        }
        if (remainders.Count == 0 || !remainders.Values.Intersect(divisions.Values.Select(static division => division.Key)).Any())
        {
            return body;
        }

        var indices = Enumerable.Range(0, instructions.Length).ToDictionary(index => instructions[index].Offset);
        var keys = divisions.Values.Select(static division => division.Key).ToHashSet();
        // Available quotients at each block's entry: the meet of its predecessors' exits, from all for every block but
        // the entry and the handlers and filters, which start with none: an exception enters them from anywhere in
        // their try, and a handler's own loop may branch back to its first instruction.
        var handlers = body.ExceptionRegions
            .SelectMany(static region => region.FilterOffset is { } filter ? [region.HandlerOffset, filter] : new[] { region.HandlerOffset })
            .ToHashSet();
        var entries = new Dictionary<int, HashSet<Key>>();
        var exits = new Dictionary<int, HashSet<Key>>();
        var order = graph.Blocks.Where(block => graph.ReachableBlocks.Contains(block.Index)).Select(static block => block.Index).ToArray();
        foreach (var block in order) exits[block] = [.. keys];
        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var block in order)
            {
                // Every other reachable block has a reachable predecessor.
                var entry = block == graph.Entry.Index || handlers.Contains(graph.GetBlock(block).StartOffset)
                    ? []
                    : graph.Predecessors[block].Where(exits.ContainsKey).Select(predecessor => exits[predecessor])
                        .Aggregate(static (meet, exit) => [.. meet.Intersect(exit)]);
                entries[block] = entry;
                var exit = new HashSet<Key>(entry);
                foreach (var instruction in graph.GetBlock(block).Instructions)
                {
                    Transfer(exit, indices[instruction.Offset], instructions, divisions);
                }
                if (!exit.SetEquals(exits[block]))
                {
                    exits[block] = exit;
                    changed = true;
                }
            }
        }

        var reused = new HashSet<int>();
        foreach (var block in order)
        {
            var available = new HashSet<Key>(entries[block]);
            foreach (var instruction in graph.GetBlock(block).Instructions)
            {
                var index = indices[instruction.Offset];
                if (remainders.TryGetValue(index, out var key) && available.Contains(key)) reused.Add(index);
                Transfer(available, index, instructions, divisions);
            }
        }
        if (reused.Count == 0) return body;

        var locals = body.Locals.ToBuilder();
        var localTypes = body.LocalSignatureTypes.ToBuilder();
        var quotients = new Dictionary<Key, int>();
        foreach (var key in reused.Select(index => remainders[index]).Distinct())
        {
            var kind = divisions.Values.First(division => division.Key == key).Kind;
            locals.Add(kind);
            localTypes.Add(CliTypeIdentity.FromStackKind(kind));
            quotients[key] = locals.Count - 1;
        }
        var replacements = instructions.Select(static instruction => new List<CilInstruction> { instruction }).ToArray();
        foreach (var (index, (key, _)) in divisions)
        {
            if (quotients.TryGetValue(key, out var local))
            {
                replacements[index] = [instructions[index], Instruction(CilOperation.StoreLocal, local), Instruction(CilOperation.LoadLocal, local)];
            }
        }
        // dividend, quotient, divisor, multiply, subtract: the quotient goes in under the divisor.
        foreach (var index in reused)
        {
            replacements[index - 1] = [Instruction(CilOperation.LoadLocal, quotients[remainders[index]]), instructions[index - 1]];
            replacements[index] = [new(0, 1, CilOperation.Multiply, new CilOperand.None()), new(0, 1, CilOperation.Subtract, new CilOperand.None())];
        }
        return RenumberedInstructions.Splice(body, replacements) with
        {
            MaxStack = body.MaxStack + 1,
            Locals = locals.ToImmutable(),
            LocalSignatureTypes = localTypes.ToImmutable(),
        };
    }

    private static CilInstruction Instruction(CilOperation operation, int local) =>
        new(0, 1, operation, new CilOperand.Index(local));

    private static Operand? OperandOf(CilInstruction instruction) => instruction switch
    {
        { Operation: CilOperation.LoadLocal or CilOperation.LoadArgument, Operand: CilOperand.Index { Value: var slot } } =>
            new Operand(instruction.Operation, slot),
        { Operation: CilOperation.LoadInt32, Operand: CilOperand.ConstantI4 { Value: var value } } =>
            new Operand(CilOperation.LoadInt32, value),
        { Operation: CilOperation.LoadInt64, Operand: CilOperand.ConstantI8 { Value: var value } } =>
            new Operand(CilOperation.LoadInt64, value),
        _ => null,
    };

    // The slot loads no quotient may hold: slots whose address the method takes and slots a handler or filter stores.
    private static HashSet<Operand> Excluded(CilMethodBody body)
    {
        var excluded = new HashSet<Operand>();
        foreach (var instruction in body.Instructions)
        {
            var load = instruction.Operation switch
            {
                CilOperation.LoadLocalAddress => CilOperation.LoadLocal,
                CilOperation.LoadArgumentAddress => CilOperation.LoadArgument,
                CilOperation.StoreLocal when InHandler(body, instruction.Offset) => CilOperation.LoadLocal,
                CilOperation.StoreArgument when InHandler(body, instruction.Offset) => CilOperation.LoadArgument,
                _ => (CilOperation?)null,
            };
            if (load is { } operation) excluded.Add(new(operation, ((CilOperand.Index)instruction.Operand).Value));
        }
        return excluded;
    }

    private static bool InHandler(CilMethodBody body, int offset) => body.ExceptionRegions.Any(region =>
        offset >= region.HandlerOffset && offset < region.HandlerOffset + region.HandlerLength ||
        region.FilterOffset is { } filter && offset >= filter && offset < region.HandlerOffset);

    // What the instruction leaves available: a store kills the quotients of its slot, then a division adds its own.
    private static void Transfer(HashSet<Key> available, int index, ImmutableArray<CilInstruction> instructions,
        Dictionary<int, (Key Key, CliValueKind Kind)> divisions)
    {
        var instruction = instructions[index];
        if (instruction.Operation is CilOperation.StoreLocal or CilOperation.StoreArgument)
        {
            var stored = new Operand(
                instruction.Operation == CilOperation.StoreLocal ? CilOperation.LoadLocal : CilOperation.LoadArgument,
                ((CilOperand.Index)instruction.Operand).Value);
            available.RemoveWhere(key => key.Dividend == stored || key.Divisor == stored);
        }
        if (divisions.TryGetValue(index, out var division)) available.Add(division.Key);
    }
}
