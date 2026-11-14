using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Constants;

// A null reference is a constant too: the load an unwritten reference-typed static field folds to (see
// UnwrittenStaticFieldProgramReducer), a dup of it, and a load of a local it was stored to decide brtrue and brfalse
// and compare with another null. A null local's loads stay loads (an int32 local's become int32 constants), and a
// null feeds no arithmetic.
internal sealed class ConstantBranchRewriter(IControlFlowGraphBuilder graphs) : IMethodRewriteRule
{
    private readonly IControlFlowGraphBuilder _graphs = graphs ?? throw new ArgumentNullException(nameof(graphs));

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var graph = _graphs.Build(body);
        var rewritten = body.Instructions.ToDictionary(instruction => instruction.Offset);
        var changed = false;
        foreach (var block in graph.Blocks)
        {
            var locals = new Dictionary<int, int>();
            var producers = new List<CilInstruction>();
            // The producers that push a null reference: a ldnull, a dup of one, or a load of a null local.
            var nulls = new HashSet<int>();
            var nullLocals = new HashSet<int>();
            // The dup that copied each null producer, and the producers already folded away: a dup still read after
            // the producer it copied goes pushes the null itself.
            var copies = new Dictionary<int, CilInstruction>();
            var removed = new HashSet<int>();
            void Remove(CilInstruction operand)
            {
                rewritten[operand.Offset] = operand with { Operation = CilOperation.Nop, Operand = new CilOperand.None() };
                removed.Add(operand.Offset);
                if (copies.TryGetValue(operand.Offset, out var copy) && !removed.Contains(copy.Offset))
                {
                    rewritten[copy.Offset] = copy with { Operation = CilOperation.LoadNull, Operand = new CilOperand.None() };
                }
            }

            foreach (var original in block.Instructions)
            {
                var instruction = original;
                if (instruction.Operation == CilOperation.Nop) continue;
                if (instruction is { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index local } &&
                    locals.TryGetValue(local.Value, out var constant))
                {
                    instruction = instruction with { Operation = CilOperation.LoadInt32, Operand = new CilOperand.ConstantI4(constant) };
                    rewritten[instruction.Offset] = instruction;
                    changed = true;
                }

                if (instruction is { Operation: CilOperation.LoadInt32, Operand: CilOperand.ConstantI4 })
                {
                    producers.Add(instruction);
                    continue;
                }

                if (instruction is { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index nullLocal } &&
                    nullLocals.Contains(nullLocal.Value))
                {
                    producers.Add(instruction);
                    nulls.Add(instruction.Offset);
                    continue;
                }

                if (instruction.Operation == CilOperation.LoadNull)
                {
                    producers.Add(instruction);
                    nulls.Add(instruction.Offset);
                    continue;
                }

                if (instruction.Operation == CilOperation.Duplicate && producers.Count > 0 && nulls.Contains(producers[^1].Offset))
                {
                    copies[producers[^1].Offset] = instruction;
                    producers.Add(instruction);
                    nulls.Add(instruction.Offset);
                    continue;
                }

                if (instruction is { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index stored })
                {
                    if (producers.Count > 0 && nulls.Contains(producers[^1].Offset))
                    {
                        producers.RemoveAt(producers.Count - 1);
                        locals.Remove(stored.Value);
                        nullLocals.Add(stored.Value);
                    }
                    else if (producers.Count > 0)
                    {
                        locals[stored.Value] = ((CilOperand.ConstantI4)producers[^1].Operand).Value;
                        producers.RemoveAt(producers.Count - 1);
                        nullLocals.Remove(stored.Value);
                    }
                    else
                    {
                        locals.Remove(stored.Value);
                        nullLocals.Remove(stored.Value);
                    }
                    continue;
                }

                if (instruction.Operation == CilOperation.CompareEqual && producers.Count >= 2 &&
                    nulls.Contains(producers[^1].Offset) == nulls.Contains(producers[^2].Offset))
                {
                    var right = producers[^1];
                    var left = producers[^2];
                    Remove(left);
                    Remove(right);
                    var equal = Value(left, nulls) == Value(right, nulls);
                    instruction = instruction with { Operation = CilOperation.LoadInt32, Operand = new CilOperand.ConstantI4(equal ? 1 : 0) };
                    rewritten[instruction.Offset] = instruction;
                    producers.RemoveRange(producers.Count - 2, 2);
                    producers.Add(instruction);
                    changed = true;
                    continue;
                }

                if (producers.Count >= 1 && !nulls.Contains(producers[^1].Offset) &&
                    instruction.Operation is CilOperation.Negate or CilOperation.OnesComplement)
                {
                    var operand = producers[^1];
                    var value = ((CilOperand.ConstantI4)operand.Operand).Value;
                    Remove(operand);
                    producers.RemoveAt(producers.Count - 1);
                    instruction = instruction with
                    {
                        Operation = CilOperation.LoadInt32,
                        Operand = new CilOperand.ConstantI4(instruction.Operation == CilOperation.Negate ? unchecked(-value) : ~value),
                    };
                    rewritten[instruction.Offset] = instruction;
                    producers.Add(instruction);
                    changed = true;
                    continue;
                }

                if (producers.Count >= 2 &&
                    (nulls.Contains(producers[^2].Offset), nulls.Contains(producers[^1].Offset)) is var (leftNull, rightNull) &&
                    (leftNull == rightNull) &&
                    (!leftNull || instruction.Operation is CilOperation.CompareGreaterThanUnsigned or
                        CilOperation.BranchIfEqual or CilOperation.BranchIfNotEqual) &&
                    Binary(instruction.Operation, Value(producers[^2], nulls), Value(producers[^1], nulls)) is { } result)
                {
                    foreach (var operand in (ReadOnlySpan<CilInstruction>)[producers[^2], producers[^1]])
                    {
                        Remove(operand);
                    }
                    producers.RemoveRange(producers.Count - 2, 2);
                    if (instruction.Operand is CilOperand.BranchTarget)
                    {
                        rewritten[instruction.Offset] = instruction with
                        {
                            Operation = result != 0 ? CilOperation.Branch : CilOperation.Nop,
                            Operand = result != 0 ? instruction.Operand : new CilOperand.None(),
                        };
                    }
                    else
                    {
                        instruction = instruction with { Operation = CilOperation.LoadInt32, Operand = new CilOperand.ConstantI4(result) };
                        rewritten[instruction.Offset] = instruction;
                        producers.Add(instruction);
                        changed = true;
                        continue;
                    }
                    changed = true;
                }

                if (instruction.Operation is CilOperation.BranchIfTrue or CilOperation.BranchIfFalse && producers.Count > 0)
                {
                    var condition = producers[^1];
                    var value = Value(condition, nulls) != 0;
                    var taken = value == (instruction.Operation == CilOperation.BranchIfTrue);
                    Remove(condition);
                    rewritten[instruction.Offset] = instruction with
                    {
                        Operation = taken ? CilOperation.Branch : CilOperation.Nop,
                        Operand = taken ? instruction.Operand : new CilOperand.None(),
                    };
                    changed = true;
                }

                // No inference through effects, aliases or unknown stack transformations.
                producers.Clear();
                nulls.Clear();
                locals.Clear();
                nullLocals.Clear();
            }
        }

        return changed ? body with { Instructions = [.. body.Instructions.Select(instruction => rewritten[instruction.Offset])] } : body;
    }

    // A producer's value: its int32 constant, or 0 for a null reference.
    private static int Value(CilInstruction producer, HashSet<int> nulls) =>
        nulls.Contains(producer.Offset) ? 0 : ((CilOperand.ConstantI4)producer.Operand).Value;

    // The int32 result of a binary operation, comparison or two-operand branch
    // (1 when taken) on constants, wrapping as the instruction does; null when
    // the instruction is none of these or would throw.
    private static int? Binary(CilOperation operation, int left, int right) => operation switch
    {
        CilOperation.Add => unchecked(left + right),
        CilOperation.Subtract => unchecked(left - right),
        CilOperation.Multiply => unchecked(left * right),
        CilOperation.Divide when right != 0 && !(left == int.MinValue && right == -1) => left / right,
        CilOperation.Remainder when right != 0 && !(left == int.MinValue && right == -1) => left % right,
        CilOperation.DivideUnsigned when right != 0 => (int)((uint)left / (uint)right),
        CilOperation.RemainderUnsigned when right != 0 => (int)((uint)left % (uint)right),
        CilOperation.BitwiseAnd => left & right,
        CilOperation.BitwiseOr => left | right,
        CilOperation.BitwiseXor => left ^ right,
        // Wasm masks the count, as the emitted shift does.
        CilOperation.ShiftLeft => left << (right & 31),
        CilOperation.ShiftRightSigned => left >> (right & 31),
        CilOperation.ShiftRightUnsigned => (int)((uint)left >> (right & 31)),
        CilOperation.CompareGreaterThanSigned => left > right ? 1 : 0,
        CilOperation.CompareGreaterThanUnsigned => (uint)left > (uint)right ? 1 : 0,
        CilOperation.CompareLessThanSigned => left < right ? 1 : 0,
        CilOperation.CompareLessThanUnsigned => (uint)left < (uint)right ? 1 : 0,
        CilOperation.BranchIfEqual => left == right ? 1 : 0,
        CilOperation.BranchIfNotEqual => left != right ? 1 : 0,
        CilOperation.BranchIfGreaterThanSigned => left > right ? 1 : 0,
        CilOperation.BranchIfGreaterThanUnsigned => (uint)left > (uint)right ? 1 : 0,
        CilOperation.BranchIfGreaterThanOrEqualSigned => left >= right ? 1 : 0,
        CilOperation.BranchIfGreaterThanOrEqualUnsigned => (uint)left >= (uint)right ? 1 : 0,
        CilOperation.BranchIfLessThanSigned => left < right ? 1 : 0,
        CilOperation.BranchIfLessThanUnsigned => (uint)left < (uint)right ? 1 : 0,
        CilOperation.BranchIfLessThanOrEqualSigned => left <= right ? 1 : 0,
        CilOperation.BranchIfLessThanOrEqualUnsigned => (uint)left <= (uint)right ? 1 : 0,
        _ => null,
    };
}
