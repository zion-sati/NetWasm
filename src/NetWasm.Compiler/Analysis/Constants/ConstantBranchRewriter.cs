using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Constants;

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

                if (instruction is { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index stored })
                {
                    if (producers.Count > 0)
                    {
                        locals[stored.Value] = ((CilOperand.ConstantI4)producers[^1].Operand).Value;
                        producers.RemoveAt(producers.Count - 1);
                    }
                    else
                    {
                        locals.Remove(stored.Value);
                    }
                    continue;
                }

                if (instruction.Operation == CilOperation.CompareEqual && producers.Count >= 2)
                {
                    var right = producers[^1];
                    var left = producers[^2];
                    rewritten[left.Offset] = left with { Operation = CilOperation.Nop, Operand = new CilOperand.None() };
                    rewritten[right.Offset] = right with { Operation = CilOperation.Nop, Operand = new CilOperand.None() };
                    var equal = ((CilOperand.ConstantI4)left.Operand).Value == ((CilOperand.ConstantI4)right.Operand).Value;
                    instruction = instruction with { Operation = CilOperation.LoadInt32, Operand = new CilOperand.ConstantI4(equal ? 1 : 0) };
                    rewritten[instruction.Offset] = instruction;
                    producers.RemoveRange(producers.Count - 2, 2);
                    producers.Add(instruction);
                    changed = true;
                    continue;
                }

                if (producers.Count >= 1 && instruction.Operation is CilOperation.Negate or CilOperation.OnesComplement)
                {
                    var operand = producers[^1];
                    var value = ((CilOperand.ConstantI4)operand.Operand).Value;
                    rewritten[operand.Offset] = operand with { Operation = CilOperation.Nop, Operand = new CilOperand.None() };
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

                if (producers.Count >= 2 && Binary(instruction.Operation,
                        ((CilOperand.ConstantI4)producers[^2].Operand).Value,
                        ((CilOperand.ConstantI4)producers[^1].Operand).Value) is { } result)
                {
                    foreach (var operand in (ReadOnlySpan<CilInstruction>)[producers[^2], producers[^1]])
                    {
                        rewritten[operand.Offset] = operand with { Operation = CilOperation.Nop, Operand = new CilOperand.None() };
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
                    var value = ((CilOperand.ConstantI4)condition.Operand).Value != 0;
                    var taken = value == (instruction.Operation == CilOperation.BranchIfTrue);
                    rewritten[condition.Offset] = condition with { Operation = CilOperation.Nop, Operand = new CilOperand.None() };
                    rewritten[instruction.Offset] = instruction with
                    {
                        Operation = taken ? CilOperation.Branch : CilOperation.Nop,
                        Operand = taken ? instruction.Operand : new CilOperand.None(),
                    };
                    changed = true;
                }

                // No inference through effects, aliases or unknown stack transformations.
                producers.Clear();
                locals.Clear();
            }
        }

        return changed ? body with { Instructions = [.. body.Instructions.Select(instruction => rewritten[instruction.Offset])] } : body;
    }

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
