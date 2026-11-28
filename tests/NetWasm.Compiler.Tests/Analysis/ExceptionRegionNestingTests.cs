// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Analysis;

public sealed class ExceptionRegionNestingTests
{
    // 0: nop; try { 1: nop; try { 2: nop; 3: leave 5 } finally { 4: endfinally } 5: nop; 6: leave 9 }
    // catch { 7: pop; 8: leave 9 } 9: ret
    private const int Inner = 0;
    private const int Outer = 1;

    private static readonly ImmutableArray<CilExceptionRegion> Regions =
    [
        new(CilExceptionRegionKind.Finally, 2, 2, 4, 1, null, null),
        new(CilExceptionRegionKind.Catch, 1, 6, 7, 2, null, null),
    ];

    private static readonly ImmutableArray<CilInstruction> Instructions =
    [
        I(0, CilOperation.Nop),
        I(1, CilOperation.Nop),
        I(2, CilOperation.Nop),
        I(3, CilOperation.Leave, new CilOperand.BranchTarget(5)),
        I(4, CilOperation.EndFinally),
        I(5, CilOperation.Nop),
        I(6, CilOperation.Leave, new CilOperand.BranchTarget(9)),
        I(7, CilOperation.Pop),
        I(8, CilOperation.Leave, new CilOperand.BranchTarget(9)),
        I(9, CilOperation.Return),
    ];

    [Fact]
    public void ATransferLeavesTheBlocksAroundItsSourceButNotItsTargetInnermostFirst()
    {
        var nesting = new ExceptionRegionNesting(Instructions, Regions);

        Assert.Equal([Inner], nesting.TriesLeft(3, 5));
        Assert.Equal([Inner, Outer], nesting.TriesLeft(3, 9));
        Assert.Equal([Outer], nesting.TriesLeft(6, 9));
        Assert.Empty(nesting.TriesLeft(0, 9));
        Assert.Empty(nesting.TriesLeft(3, 2));
        Assert.Equal(Outer, nesting.EnclosingTry(Inner));
        Assert.Equal(-1, nesting.EnclosingTry(Outer));
    }

    [Fact]
    public void HandlersCoverTheirInstructionsOnly()
    {
        var nesting = new ExceptionRegionNesting(Instructions, Regions);

        Assert.True(nesting.InHandler(4));
        Assert.True(nesting.InHandler(7));
        Assert.True(nesting.InHandler(8));
        Assert.False(nesting.InHandler(2));
        Assert.False(nesting.InHandler(5));
        Assert.False(nesting.InHandler(9));
        Assert.False(nesting.HandlerHoldsABlock(Inner));
        Assert.False(nesting.HandlerHoldsABlock(Outer));
        Assert.Equal(10, nesting.Position(10));
    }

    // try { 0: leave 7 } catch { 1: pop; try { 2: leave 5 } finally { 3: nop; 4: endfinally } 5: leave 7; 6: nop }
    // 7: ret
    [Fact]
    public void AHandlerHoldingAProtectedBlockCoversIt()
    {
        ImmutableArray<CilExceptionRegion> regions =
        [
            new(CilExceptionRegionKind.Catch, 0, 1, 1, 6, null, null),
            new(CilExceptionRegionKind.Finally, 2, 1, 3, 2, null, null),
        ];
        ImmutableArray<CilInstruction> instructions =
        [
            I(0, CilOperation.Leave, new CilOperand.BranchTarget(7)),
            I(1, CilOperation.Pop),
            I(2, CilOperation.Leave, new CilOperand.BranchTarget(5)),
            I(3, CilOperation.Nop),
            I(4, CilOperation.EndFinally),
            I(5, CilOperation.Leave, new CilOperand.BranchTarget(7)),
            I(6, CilOperation.Nop),
            I(7, CilOperation.Return),
        ];

        var nesting = new ExceptionRegionNesting(instructions, regions);

        Assert.True(nesting.HandlerHoldsABlock(0));
        Assert.False(nesting.HandlerHoldsABlock(1));
        Assert.True(nesting.InHandler(2));
        Assert.Equal(-1, nesting.EnclosingTry(1));
        Assert.Equal([1], nesting.TriesLeft(2, 5));
    }

    private static CilInstruction I(int offset, CilOperation operation, CilOperand? operand = null) =>
        new(offset, offset + 1, operation, operand ?? new CilOperand.None());
}
