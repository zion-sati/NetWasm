using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal interface IConditionalStoreMerger
{
    CilMethodBody Rewrite(CilMethodBody body);
}

// `if (c) s.f = a; else s.f = b;` stores to one field on both arms, as SciMark's
// random generator steps its indexes (`if (_i == 0) _i = 16; else _i--;`). Each
// arm now only computes its value, and one store follows where the arms meet,
// the shape Roslyn gives `s.f = c ? a : b`: Binaryen turns its two values into
// a select, with no branch to mispredict. The arms are Roslyn's: the condition
// branches to the second arm, the first ends by jumping past the second, and
// each loads the same local or argument, computes its value from loads,
// constants and arithmetic, and stores it to the same field, or both store
// one static field. An arm that reads the field through a copy of the object,
// as Roslyn compiles `s.f--`, reads it through the slot instead. No other jump lands inside an arm, and the slot is not
// stored there, so reading it after the value reads the same object.
internal sealed class ConditionalStoreMerger(IFieldRepository fields, bool enabled) : IConditionalStoreMerger
{
    private readonly IFieldRepository _fields = fields ?? throw new ArgumentNullException(nameof(fields));

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!enabled || !body.ExceptionRegions.IsEmpty) return body;
        var instructions = body.Instructions;
        var indices = Enumerable.Range(0, instructions.Length).ToDictionary(index => instructions[index].Offset);
        var targets = new Dictionary<int, int>();
        foreach (var instruction in instructions)
        {
            foreach (var target in Targets(instruction)) targets[target] = targets.GetValueOrDefault(target) + 1;
        }
        List<CilInstruction>[]? replacements = null;
        var locals = body.Locals.ToBuilder();
        var localTypes = body.LocalSignatureTypes.ToBuilder();
        var claimed = new HashSet<int>();
        for (var branch = 0; branch < instructions.Length; branch++)
        {
            if (Diamond(instructions, indices, targets, branch) is not var (elseStart, end, store, owner, type) ||
                Enumerable.Range(branch, end - branch + 1).Any(claimed.Contains))
            {
                continue;
            }
            replacements ??= [.. instructions.Select(static instruction => new List<CilInstruction> { instruction })];
            locals.Add(type.StackKind);
            localTypes.Add(type);
            var value = locals.Count - 1;
            var jump = elseStart - 1;
            var receiver = owner is null ? 0 : 1;
            // The first arm keeps its value and jumps to the store; the second falls into it.
            if (owner is not null) replacements[branch + 1] = [];
            replacements[jump - 1] = [];
            replacements[jump] = [instructions[jump] with { Operand = new CilOperand.BranchTarget(instructions[end].Offset) }];
            if (owner is not null)
            {
                replacements[elseStart] = [];
                // A copy of the object the arm began with is the slot's object.
                foreach (var start in (int[])[branch + 2, elseStart + 1])
                {
                    if (instructions[start].Operation == CilOperation.Duplicate) replacements[start] = [owner];
                }
            }
            replacements[end] =
            [
                new(0, 1, CilOperation.StoreLocal, new CilOperand.Index(value)),
                .. receiver == 1 ? [owner!] : Array.Empty<CilInstruction>(),
                new(0, 1, CilOperation.LoadLocal, new CilOperand.Index(value)),
                store,
            ];
            claimed.UnionWith(Enumerable.Range(branch, end - branch + 1));
        }
        return replacements is null
            ? body
            : RenumberedInstructions.Splice(body, replacements) with
            {
                Locals = locals.ToImmutable(),
                LocalSignatureTypes = localTypes.ToImmutable(),
            };
    }

    // The second arm's first index, the second arm's store index, the store, the
    // slot load both arms begin with (null for a static) and the value's type.
    private (int ElseStart, int End, CilInstruction Store, CilInstruction? Owner, CliTypeIdentity Type)? Diamond(
        ImmutableArray<CilInstruction> instructions, Dictionary<int, int> indices, Dictionary<int, int> targets, int branch)
    {
        if (!IsConditional(instructions[branch].Operation) ||
            instructions[branch].Operand is not CilOperand.BranchTarget { Offset: var elseOffset } ||
            !indices.TryGetValue(elseOffset, out var elseStart) || elseStart <= branch + 2 ||
            instructions[elseStart - 1] is not { Operation: CilOperation.Branch, Operand: CilOperand.BranchTarget { Offset: var endOffset } } ||
            !indices.TryGetValue(endOffset, out var after) || after <= elseStart + 1)
        {
            return null;
        }
        var end = after - 1;
        var (first, second) = (instructions[elseStart - 2], instructions[end]);
        if (first.Operation is not (CilOperation.StoreField or CilOperation.StoreStaticField) ||
            first.Operation != second.Operation ||
            FieldOperand.Describe(first.Operand, _fields) is not { } field ||
            FieldOperand.Describe(second.Operand, _fields)?.Field != field.Field ||
            field.Type.StackKind is CliValueKind.ValueType or CliValueKind.Unknown ||
            targets.GetValueOrDefault(elseOffset) != 1)
        {
            return null;
        }
        CilInstruction? owner = null;
        int firstValue = branch + 1, secondValue = elseStart;
        if (first.Operation == CilOperation.StoreField)
        {
            owner = instructions[branch + 1];
            if (owner.Operation is not (CilOperation.LoadLocal or CilOperation.LoadArgument) ||
                instructions[elseStart].Operation != owner.Operation || instructions[elseStart].Operand != owner.Operand)
            {
                return null;
            }
            (firstValue, secondValue) = (branch + 2, elseStart + 1);
        }
        // No jump lands inside an arm but the condition's, at the second arm's
        // start, and each value is computed without branching from an empty stack.
        return Enumerable.Range(branch + 1, end - branch).All(index =>
                index == elseStart || !targets.ContainsKey(instructions[index].Offset)) &&
            Value(instructions, firstValue, elseStart - 2, owner) && Value(instructions, secondValue, end, owner)
            ? (elseStart, end, first, owner, field.Type)
            : null;
    }

    // Loads, constants and arithmetic that leave one value, so nothing in the
    // arm stores the slot.
    private static bool Value(ImmutableArray<CilInstruction> instructions, int start, int end, CilInstruction? owner)
    {
        var depth = 0;
        for (var index = start; index < end; index++)
        {
            var instruction = instructions[index];
            if (owner is not null && index == start && instruction.Operation == CilOperation.Duplicate)
            {
                depth = 1;
                continue;
            }
            int pops, pushes;
            switch (instruction.Operation)
            {
                case CilOperation.LoadLocal or CilOperation.LoadArgument or CilOperation.LoadNull or CilOperation.LoadStaticField or
                    >= CilOperation.LoadInt32 and <= CilOperation.LoadFloat64:
                    (pops, pushes) = (0, 1);
                    break;
                case CilOperation.LoadField or CilOperation.Negate or CilOperation.OnesComplement or
                    >= CilOperation.ConvertInt32 and <= CilOperation.ConvertNumeric:
                    (pops, pushes) = (1, 1);
                    break;
                case >= CilOperation.Add and <= CilOperation.RemainderUnsigned:
                case >= CilOperation.CompareEqual and <= CilOperation.CompareLessThanUnsigned:
                    (pops, pushes) = (2, 1);
                    break;
                default:
                    return false;
            }
            if (pops > depth) return false;
            depth += pushes - pops;
        }
        return depth == 1;
    }

    private static bool IsConditional(CilOperation operation) =>
        operation is >= CilOperation.BranchIfTrue and <= CilOperation.BranchIfLessThanOrEqualUnsigned;

    private static ImmutableArray<int> Targets(CilInstruction instruction) =>
        instruction.Operand is CilOperand.BranchTarget { Offset: var offset } ? [offset] : [];
}
