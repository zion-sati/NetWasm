// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests.Analysis;

public sealed class FinallyClonerTests
{
    private static readonly AssemblyIdentity Assembly = new("FinallyClonerTests");

    // int x; try { x = 1; } finally { x = 2; } return x;
    // The leave goes to a copy of the handler placed after it, whose endfinally goes on to the target, and the handler
    // becomes a fault: it runs only when an exception leaves the block.
    [Fact]
    public void ALeaveRunsACopyOfTheFinallyAndTheHandlerBecomesAFault()
    {
        var body = Body(
            [Region(CilExceptionRegionKind.Finally, 0, 3, 3, 3)],
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(1, CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(2, CilOperation.Leave, new CilOperand.BranchTarget(6)),
            I(3, CilOperation.LoadInt32, new CilOperand.ConstantI4(2)),
            I(4, CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(5, CilOperation.EndFinally),
            I(6, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(7, CilOperation.Return));

        var result = new FinallyCloner(64).Rewrite(body);

        Assert.Equal(new CilExceptionRegion(CilExceptionRegionKind.Fault, 0, 3, 3, 3, null, null), Assert.Single(result.ExceptionRegions));
        Assert.Equal(
            [
                (CilOperation.LoadInt32, (CilOperand)new CilOperand.ConstantI4(1)),
                (CilOperation.StoreLocal, new CilOperand.Index(0)),
                (CilOperation.Leave, new CilOperand.BranchTarget(6)),
                (CilOperation.LoadInt32, new CilOperand.ConstantI4(2)),
                (CilOperation.StoreLocal, new CilOperand.Index(0)),
                (CilOperation.EndFinally, new CilOperand.None()),
                // The copy: its endfinally goes to its exit, which branches to the target (it leaves no block).
                (CilOperation.LoadInt32, new CilOperand.ConstantI4(2)),
                (CilOperation.StoreLocal, new CilOperand.Index(0)),
                (CilOperation.Branch, new CilOperand.BranchTarget(9)),
                (CilOperation.Branch, new CilOperand.BranchTarget(10)),
                (CilOperation.LoadLocal, new CilOperand.Index(0)),
                (CilOperation.Return, new CilOperand.None()),
            ],
            result.Instructions.Select(instruction => (instruction.Operation, instruction.Operand)));
        // A copy reports the handler's source offsets, so its lines and stack frames are the finally's.
        Assert.Equal(3, result.Instructions[6].SourceOffset);
    }

    // try { if (c) leave A; leave B; } finally { x = 2; }: one copy per target.
    [Fact]
    public void EachTargetHasItsOwnCopy()
    {
        var body = Body(
            [Region(CilExceptionRegionKind.Finally, 0, 4, 4, 3)],
            I(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(1, CilOperation.BranchIfFalse, new CilOperand.BranchTarget(3)),
            I(2, CilOperation.Leave, new CilOperand.BranchTarget(7)),
            I(3, CilOperation.Leave, new CilOperand.BranchTarget(9)),
            I(4, CilOperation.LoadInt32, new CilOperand.ConstantI4(2)),
            I(5, CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(6, CilOperation.EndFinally),
            I(7, CilOperation.LoadInt32, new CilOperand.ConstantI4(7)),
            I(8, CilOperation.Return),
            I(9, CilOperation.LoadInt32, new CilOperand.ConstantI4(9)),
            I(10, CilOperation.Return));

        var result = new FinallyCloner(64).Rewrite(body);

        var region = Assert.Single(result.ExceptionRegions);
        Assert.Equal(CilExceptionRegionKind.Fault, region.Kind);
        // Copies for targets 7 and 9 at 7 and 11, each three instructions and an exit; the originals follow from 15.
        Assert.Equal(new CilOperand.BranchTarget(7), result.Instructions[2].Operand);
        Assert.Equal(new CilOperand.BranchTarget(11), result.Instructions[3].Operand);
        Assert.Equal(new CilOperand.BranchTarget(15), result.Instructions[10].Operand);
        Assert.Equal(new CilOperand.BranchTarget(17), result.Instructions[14].Operand);
        Assert.Equal((CilOperation.LoadInt32, (CilOperand)new CilOperand.ConstantI4(7)),
            (result.Instructions[15].Operation, result.Instructions[15].Operand));
        Assert.Equal((CilOperation.LoadInt32, (CilOperand)new CilOperand.ConstantI4(9)),
            (result.Instructions[17].Operation, result.Instructions[17].Operand));
    }

    // try { try { leave T; } finally { x = 1; } } finally { x = 2; }: the leave runs the inner copy, which leaves for
    // the outer copy (it exits the outer block), which goes to the target. The inner copy stays inside the outer block.
    [Fact]
    public void ALeaveOutOfNestedFinallysRunsTheirCopiesInTurn()
    {
        var body = Body(
            [Region(CilExceptionRegionKind.Finally, 0, 1, 1, 3), Region(CilExceptionRegionKind.Finally, 0, 5, 5, 3)],
            I(0, CilOperation.Leave, new CilOperand.BranchTarget(8)),
            I(1, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(2, CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(3, CilOperation.EndFinally),
            I(4, CilOperation.Leave, new CilOperand.BranchTarget(8)),
            I(5, CilOperation.LoadInt32, new CilOperand.ConstantI4(2)),
            I(6, CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(7, CilOperation.EndFinally),
            I(8, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(9, CilOperation.Return));

        var result = new FinallyCloner(64).Rewrite(body);

        // Inner copy at 4..7 (before the outer try's leave, inside the outer block); outer copy at 12..15.
        Assert.All(result.ExceptionRegions, region => Assert.Equal(CilExceptionRegionKind.Fault, region.Kind));
        Assert.Equal(new CilOperand.BranchTarget(4), result.Instructions[0].Operand);
        Assert.Equal((CilOperation.Leave, (CilOperand)new CilOperand.BranchTarget(12)),
            (result.Instructions[7].Operation, result.Instructions[7].Operand));
        var outer = result.ExceptionRegions[1];
        Assert.True(outer.TryOffset <= 4 && outer.TryOffset + outer.TryLength > 7);
        Assert.Equal((CilOperation.Leave, (CilOperand)new CilOperand.BranchTarget(12)),
            (result.Instructions[8].Operation, result.Instructions[8].Operand));
        Assert.Equal((CilOperation.Branch, (CilOperand)new CilOperand.BranchTarget(16)),
            (result.Instructions[15].Operation, result.Instructions[15].Operand));
        Assert.Equal(CilOperation.LoadLocal, result.Instructions[16].Operation);
    }

    // A handler over the budget, a handler holding a protected block, and a budget of 0 keep the finally.
    [Fact]
    public void KeepsAFinallyThatDoesNotQualify()
    {
        var single = Body(
            [Region(CilExceptionRegionKind.Finally, 0, 1, 1, 3)],
            I(0, CilOperation.Leave, new CilOperand.BranchTarget(4)),
            I(1, CilOperation.LoadInt32, new CilOperand.ConstantI4(2)),
            I(2, CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(3, CilOperation.EndFinally),
            I(4, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(5, CilOperation.Return));
        var nested = Body(
            [Region(CilExceptionRegionKind.Finally, 0, 1, 1, 4), Region(CilExceptionRegionKind.Fault, 1, 1, 2, 2)],
            I(0, CilOperation.Leave, new CilOperand.BranchTarget(5)),
            I(1, CilOperation.Leave, new CilOperand.BranchTarget(4)),
            I(2, CilOperation.Nop),
            I(3, CilOperation.EndFinally),
            I(4, CilOperation.EndFinally),
            I(5, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(6, CilOperation.Return));

        Assert.Same(single, new FinallyCloner(2).Rewrite(single));
        Assert.Same(single, new FinallyCloner(0).Rewrite(single));
        Assert.Same(nested, new FinallyCloner(64).Rewrite(nested));
    }

    // 20,000 of try { leave join } finally { nop } join: nop in a row: every finally is copied, each once. Each question
    // the cloner asks about a leave or a finally looks at the blocks around it, not at every block in the method.
    [Fact]
    public void CopiesEveryFinallyOfAMethodWithThousandsOfThem()
    {
        const int Count = 20_000;
        var instructions = new List<CilInstruction>();
        var regions = new List<CilExceptionRegion>();
        for (var block = 0; block < Count; block++)
        {
            var at = block * 4;
            instructions.Add(I(at, CilOperation.Leave, new CilOperand.BranchTarget(at + 3)));
            instructions.Add(I(at + 1, CilOperation.Nop));
            instructions.Add(I(at + 2, CilOperation.EndFinally));
            instructions.Add(I(at + 3, CilOperation.Nop));
            regions.Add(Region(CilExceptionRegionKind.Finally, at, 1, at + 1, 2));
        }
        instructions.Add(I(Count * 4, CilOperation.LoadLocal, new CilOperand.Index(0)));
        instructions.Add(I(Count * 4 + 1, CilOperation.Return));

        var result = new FinallyCloner(64).Rewrite(Body([.. regions], [.. instructions]));

        Assert.All(result.ExceptionRegions, region => Assert.Equal(CilExceptionRegionKind.Fault, region.Kind));
        Assert.Equal(instructions.Count + Count * 3, result.Instructions.Length);
    }

    // 1,000 finallies nested in one another, every leave out to the end: each finally's copy goes on to the next one out.
    [Fact]
    public void CopiesAThousandNestedFinallies()
    {
        const int Depth = 1_000;
        var instructions = new List<CilInstruction>();
        var regions = new List<CilExceptionRegion>();
        var handlers = Depth + 1;
        var end = handlers + 3 * Depth - 1;
        for (var level = 0; level < Depth; level++) instructions.Add(I(level, CilOperation.Nop));
        instructions.Add(I(Depth, CilOperation.Leave, new CilOperand.BranchTarget(end)));
        for (var inner = 0; inner < Depth; inner++)
        {
            var handler = handlers + 3 * inner;
            instructions.Add(I(handler, CilOperation.Nop));
            instructions.Add(I(handler + 1, CilOperation.EndFinally));
            if (inner < Depth - 1) instructions.Add(I(handler + 2, CilOperation.Leave, new CilOperand.BranchTarget(end)));
            // The finally whose handler is this one: its protected block starts at its level and ends at the handler.
            var level = Depth - 1 - inner;
            regions.Add(Region(CilExceptionRegionKind.Finally, level, handler - level, handler, 2));
        }
        instructions.Add(I(end, CilOperation.Return));

        var result = new FinallyCloner(64).Rewrite(Body([.. regions], [.. instructions]));

        Assert.All(result.ExceptionRegions, region => Assert.Equal(CilExceptionRegionKind.Fault, region.Kind));
        Assert.Equal(instructions.Count + Depth * 3, result.Instructions.Length);
        // The innermost protected block's leave goes to the innermost finally's copy, placed after its handler.
        var leave = result.Instructions[Depth];
        Assert.Equal(CilOperation.Leave, leave.Operation);
        Assert.Equal(new CilOperand.BranchTarget(handlers + 2), leave.Operand);
    }

    private static CilExceptionRegion Region(
        CilExceptionRegionKind kind, int tryOffset, int tryLength, int handlerOffset, int handlerLength) =>
        new(kind, tryOffset, tryLength, handlerOffset, handlerLength, null, null);

    private static CilInstruction I(int offset, CilOperation operation, CilOperand? operand = null) =>
        new(offset, offset + 1, operation, operand ?? new CilOperand.None());

    private static CilMethodBody Body(CilExceptionRegion[] regions, params CilInstruction[] instructions) =>
        new(new MethodDefinitionModel(new(Assembly, 0x06000001), new(Assembly, 0x02000001), "Run", true,
                MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.I4), 0),
            2, [CliValueKind.I4], [.. instructions])
        {
            ExceptionRegions = [.. regions],
        };
}
