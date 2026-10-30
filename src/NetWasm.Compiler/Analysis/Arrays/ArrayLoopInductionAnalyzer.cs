using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Arrays;

internal sealed class ArrayLoopInductionAnalyzer(
    IControlFlowNaturalLoopAnalyzer loops,
    IControlFlowDominanceAnalyzer dominance) : IArrayLoopInductionAnalyzer
{
    private readonly IControlFlowNaturalLoopAnalyzer _loops = loops ??
        throw new ArgumentNullException(nameof(loops));
    private readonly IControlFlowDominanceAnalyzer _dominance = dominance ??
        throw new ArgumentNullException(nameof(dominance));

    public ImmutableArray<ArrayLoopInduction> Analyze(ValidatedControlFlowGraph controlFlow)
    {
        ArgumentNullException.ThrowIfNull(controlFlow);
        var graph = controlFlow.Graph;
        var body = graph.MethodBody;
        if (!body.ExceptionRegions.IsEmpty) return [];
        var loops = _loops.Analyze(graph, graph.Entry.Index, graph.ReachableBlocks);
        if (loops.IsEmpty) return [];
        var dominators = _dominance.Analyze(graph, graph.Entry.Index, graph.ReachableBlocks);
        var result = ImmutableArray.CreateBuilder<ArrayLoopInduction>();
        foreach (var loop in loops)
        {
            foreach (var guardIndex in loop)
            {
                var guard = graph.GetBlock(guardIndex);
                // A standalone condition block ensures the induction is tested
                // before any body effects, including on the initial iteration.
                var code = guard.Instructions.Where(i => i.Operation != CilOperation.Nop).ToArray();
                // index + c < length, as in `for (i = 0; i + 1 < a.Length; i++)`.
                var lookahead = 0;
                if (code.Length == 7 &&
                    code[1] is { Operation: CilOperation.LoadInt32, Operand: CilOperand.ConstantI4 { Value: > 0 } added } &&
                    code[2].Operation == CilOperation.Add)
                {
                    lookahead = added.Value;
                    code = [code[0], .. code[3..]];
                }
                if (code.Length != 5 ||
                    code[0].Operation != CilOperation.LoadLocal ||
                    code[1].Operation != CilOperation.LoadLocal ||
                    code[2].Operation != CilOperation.LoadArrayLength ||
                    code[3].Operation != CilOperation.ConvertInt32 ||
                    code[4].Operation is not (CilOperation.BranchIfLessThanSigned or
                        CilOperation.BranchIfLessThanUnsigned or
                        CilOperation.BranchIfGreaterThanOrEqualSigned or
                        CilOperation.BranchIfGreaterThanOrEqualUnsigned) ||
                    code[4].Operand is not CilOperand.BranchTarget branch ||
                    !controlFlow.EntryStacks.TryGetValue(guardIndex, out var entry) || !entry.IsEmpty)
                    continue;
                var index = ((CilOperand.Index)code[0].Operand).Value;
                var array = ((CilOperand.Index)code[1].Operand).Value;
                if (body.Locals[index] != CliValueKind.I4 ||
                    body.LocalSignatureTypes[array].Shape != CliTypeShape.SzArray)
                    continue;
                var taken = graph.GetBlockAtOffset(branch.Offset).Index;
                var fallthrough = graph.GetBlockAtOffset(code[4].NextOffset).Index;
                var bodyEntry = code[4].Operation is CilOperation.BranchIfLessThanSigned or
                    CilOperation.BranchIfLessThanUnsigned ? taken : fallthrough;
                var exit = bodyEntry == taken ? fallthrough : taken;
                if (bodyEntry == guardIndex || !loop.Contains(bodyEntry) ||
                    loop.Contains(exit) || loop.Any(b => !dominators[b].Contains(guardIndex)) ||
                    // A block nothing reaches, such as the code a switch left
                    // behind its bit test, never enters the loop.
                    loop.Where(b => b != guardIndex).Any(b =>
                        graph.Predecessors[b].Any(p => !loop.Contains(p) && graph.ReachableBlocks.Contains(p))))
                    continue;
                if (body.Instructions.Any(i =>
                        i.Operation == CilOperation.LoadLocalAddress &&
                        i.Operand is CilOperand.Index exposed && (exposed.Value == index || exposed.Value == array)) ||
                    loop.SelectMany(b => graph.GetBlock(b).Instructions).Any(i =>
                        i.Operation == CilOperation.StoreLocal && i.Operand is CilOperand.Index stored && stored.Value == array))
                    continue;

                var writes = graph.Blocks.SelectMany(b => b.Instructions.Select((i, position) =>
                    (Block: b, Instruction: i, Position: position)))
                    .Where(item => item.Instruction.Operation == CilOperation.StoreLocal &&
                        item.Instruction.Operand is CilOperand.Index stored && stored.Value == index).ToArray();
                // More complex initialization and update paths need a separate
                // range proof; they must not inherit this canonical-loop fact.
                if (writes.Length != 2) continue;
                var initial = writes.Where(w => !loop.Contains(w.Block.Index)).ToArray();
                var increments = writes.Where(w => loop.Contains(w.Block.Index)).ToArray();
                if (initial.Length != 1 || increments.Length != 1) continue;
                var init = initial[0];
                var increment = increments[0];
                if (init.Position == 0 ||
                    init.Block.Instructions[init.Position - 1] is not
                    { Operation: CilOperation.LoadInt32, Operand: CilOperand.ConstantI4 { Value: >= 0 } } ||
                    !dominators[guardIndex].Contains(init.Block.Index) ||
                    increment.Position < 3 ||
                    !dominators[increment.Block.Index].Contains(bodyEntry))
                    continue;
                var step = increment.Block.Instructions;
                if (step[increment.Position - 3].Operation != CilOperation.LoadLocal ||
                    step[increment.Position - 3].Operand is not CilOperand.Index read || read.Value != index ||
                    step[increment.Position - 2] is not
                    { Operation: CilOperation.LoadInt32, Operand: CilOperand.ConstantI4 { Value: 1 } } ||
                    step[increment.Position - 1].Operation != CilOperation.Add)
                    continue;
                // The update must be the last effect before returning to the
                // condition. The length bound then proves that +1 cannot wrap.
                var tail = step.Skip(increment.Position + 1)
                    .Where(i => i.Operation != CilOperation.Nop).ToArray();
                if (graph.Successors[increment.Block.Index].Length != 1 ||
                    graph.Successors[increment.Block.Index][0] != guardIndex ||
                    (tail.Length != 0 && (tail.Length != 1 || tail[0].Operation != CilOperation.Branch)))
                    continue;
                result.Add(new(index, array, guardIndex, bodyEntry,
                    increment.Instruction.Offset, loop)
                {
                    Lookahead = lookahead,
                });
            }
        }
        return result.ToImmutable();
    }
}
