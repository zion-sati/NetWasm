using System;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Arrays;

internal sealed class ArrayLoopUnrollingPlanner(
    IArrayLoopInductionAnalyzer induction,
    IRuntimeIntrinsicRegistry intrinsics,
    int expandedBodyBudget) : IArrayLoopUnrollingPlanner
{
    private readonly IArrayLoopInductionAnalyzer _induction = induction ??
        throw new ArgumentNullException(nameof(induction));
    private readonly IRuntimeIntrinsicRegistry _intrinsics = intrinsics ??
        throw new ArgumentNullException(nameof(intrinsics));

    public ImmutableArray<ArrayLoopUnrollingPlan> Plan(ValidatedControlFlowGraph controlFlow)
    {
        ArgumentNullException.ThrowIfNull(controlFlow);
        ArgumentOutOfRangeException.ThrowIfNegative(expandedBodyBudget);
        var graph = controlFlow.Graph;
        var plans = ImmutableArray.CreateBuilder<ArrayLoopUnrollingPlan>();
        foreach (var loop in _induction.Analyze(controlFlow))
        {
            // Unrolled guards test index + factor - 1, which assumes a plain index guard.
            if (loop.LoopBlocks.Count != 2 || loop.Lookahead != 0) continue;
            var guard = graph.GetBlock(loop.GuardBlock);
            var body = graph.GetBlock(loop.BodyEntryBlock);
            if (!controlFlow.EntryStacks[body.Index].IsEmpty) continue;
            var code = body.Instructions.Where(instruction => instruction.Operation != CilOperation.Nop).ToImmutableArray();
            if (code[^1].Operation == CilOperation.Branch) code = code.RemoveAt(code.Length - 1);
            if (code.Any(instruction => !IsNumericBodyInstruction(instruction))) continue;
            var payload = code[..^4];
            var indexReads = payload.Count(instruction => instruction.Operation == CilOperation.LoadLocal &&
                instruction.Operand is CilOperand.Index index && index.Value == loop.IndexLocal);
            var factor = ExpandedCost(4) <= expandedBodyBudget ? 4
                : ExpandedCost(2) <= expandedBodyBudget ? 2 : 1;
            if (factor == 1) continue;
            var entries = graph.Predecessors[guard.Index].Where(index => !loop.LoopBlocks.Contains(index))
                .Select(index => graph.GetBlock(index).Instructions[^1]).ToArray();
            // Do not redirect fallthrough, conditional or dispatcher entries.
            // CFG predecessors already prove the target of an unconditional branch.
            if (entries.Length == 0 || entries.Any(instruction => instruction.Operation != CilOperation.Branch)) continue;
            plans.Add(new(loop.IndexLocal, loop.ArrayLocal, guard.StartOffset,
                [.. entries.Select(instruction => instruction.Offset)], code, factor)
            {
                LengthLocal = loop.LengthLocal,
            });

            long ExpandedCost(int count) => (long)payload.Length * count +
                2L * indexReads * (count - 1) + 4;
        }
        return plans.ToImmutable();
    }

    private static readonly FrozenSet<CilOperation> NumericBodyOperations = new[]
    {
        CilOperation.LoadLocal, CilOperation.StoreLocal, CilOperation.LoadArgument,
        CilOperation.LoadInt32, CilOperation.LoadInt64, CilOperation.LoadFloat32, CilOperation.LoadFloat64,
        CilOperation.Duplicate, CilOperation.Pop,
        CilOperation.Add, CilOperation.Subtract, CilOperation.Multiply,
        CilOperation.BitwiseAnd, CilOperation.BitwiseOr, CilOperation.BitwiseXor,
        CilOperation.ShiftLeft, CilOperation.ShiftRightSigned, CilOperation.ShiftRightUnsigned,
        CilOperation.Negate, CilOperation.OnesComplement,
        CilOperation.AddChecked, CilOperation.AddCheckedUnsigned,
        CilOperation.SubtractChecked, CilOperation.SubtractCheckedUnsigned,
        CilOperation.MultiplyChecked, CilOperation.MultiplyCheckedUnsigned,
        CilOperation.Divide, CilOperation.DivideUnsigned, CilOperation.Remainder, CilOperation.RemainderUnsigned,
        CilOperation.LoadArrayLength, CilOperation.LoadArrayElement, CilOperation.StoreArrayElement,
        // A struct element's fields, read through its address.
        CilOperation.LoadArrayElementAddress, CilOperation.LoadField,
    }.ToFrozenSet();

    // A span element is read or written through the address Unsafe.Add
    // computes, which, as As and AsRef, reads and writes nothing itself.
    private bool IsNumericBodyInstruction(CilInstruction instruction) => instruction.Operation switch
    {
        >= CilOperation.ConvertInt32 and <= CilOperation.ConvertNumeric => true,
        CilOperation.LoadObject or CilOperation.StoreObject =>
            CilOperands.Type(instruction.Operand).Shape == CliTypeShape.Primitive,
        CilOperation.Call => _intrinsics.TryGetIntrinsic(CilOperands.MethodKey(instruction.Operand), out var intrinsic) &&
            intrinsic is RuntimeIntrinsic.UnsafeAdd or RuntimeIntrinsic.UnsafeAddByteOffset or RuntimeIntrinsic.UnsafeAs or
                RuntimeIntrinsic.UnsafeAsRef or RuntimeIntrinsic.UnsafeAsRefManaged,
        _ => NumericBodyOperations.Contains(instruction.Operation),
    };
}
