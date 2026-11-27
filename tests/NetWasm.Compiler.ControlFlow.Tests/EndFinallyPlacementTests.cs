// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Draft;
using NetWasm.Compiler.ControlFlow.Structuring;
using NetWasm.Compiler.Core;
using Xunit;

namespace NetWasm.Compiler.ControlFlow.Tests;

// A finally handler is left by falling out of its end, so its endfinally block must end it: a handler that placed
// code after it gets that block moved to its end, reached by branches out of a labeled block around the rest.
public sealed class EndFinallyPlacementTests
{
    private static readonly BasicBlock Condition = Block(0, 0, CilOperation.BranchIfTrue);
    private static readonly BasicBlock Copy = Block(1, 4, CilOperation.Nop);
    private static readonly BasicBlock Throw = Block(2, 8, CilOperation.Throw);
    private static readonly BasicBlock EndFinally = Block(3, 12, CilOperation.EndFinally);
    private static readonly BasicBlock OtherEndFinally = Block(4, 16, CilOperation.EndFinally);

    [Fact]
    public void AHandlerWhoseEndFinallyEndsItIsReturnedAsItIs()
    {
        var handler = Sequence(
            new StructuredIfDraft(Condition, Sequence(new StructuredBlockDraft(Throw)), StructuredSequenceDraft.Empty),
            new StructuredBlockDraft(Copy),
            new StructuredBlockDraft(EndFinally));

        Assert.Same(handler, EndFinallyPlacement.EndLast(handler));
    }

    [Fact]
    public void AnEndFinallyInsideALabeledBlockMovesToTheEndAndIsReachedByABranch()
    {
        // The shape a shared throw block left: the endfinally in the labeled block, the throw block after it.
        var handler = Sequence(
            new StructuredLabeledBlockDraft(Throw.Index, Sequence(
                new StructuredIfDraft(Condition, Sequence(new StructuredLabelBranchDraft(Throw.Index)),
                    StructuredSequenceDraft.Empty),
                new StructuredBlockDraft(Copy),
                new StructuredBlockDraft(EndFinally))),
            new StructuredBlockDraft(Throw));

        var placed = EndFinallyPlacement.EndLast(handler);

        Assert.Equal(2, placed.Regions.Length);
        var labeled = Assert.IsType<StructuredLabeledBlockDraft>(placed.Regions[0]);
        Assert.Equal(EndFinally.Index, labeled.NextBlock);
        Assert.Same(EndFinally, Assert.IsType<StructuredBlockDraft>(placed.Regions[1]).Block);
        // The rest keeps its structure: the shared throw block's labeled block, then the throw block.
        Assert.Equal(2, labeled.Body.Regions.Length);
        var inner = Assert.IsType<StructuredLabeledBlockDraft>(labeled.Body.Regions[0]);
        Assert.Equal(EndFinally.Index, Assert.IsType<StructuredLabelBranchDraft>(inner.Body.Regions[^1]).TargetBlock);
        Assert.Same(Throw, Assert.IsType<StructuredBlockDraft>(labeled.Body.Regions[1]).Block);
    }

    [Fact]
    public void EveryPlaceThatReachedTheEndFinallyBranchesToIt()
    {
        // An endfinally that ended an if arm, and the same block before code that follows the if.
        var handler = Sequence(
            new StructuredIfDraft(Condition,
                Sequence(new StructuredBlockDraft(EndFinally)),
                Sequence(new StructuredBlockDraft(Copy))),
            new StructuredBlockDraft(EndFinally, IsOriginal: false),
            new StructuredBlockDraft(Throw));

        var placed = EndFinallyPlacement.EndLast(handler);

        var labeled = Assert.IsType<StructuredLabeledBlockDraft>(placed.Regions[0]);
        var conditional = Assert.IsType<StructuredIfDraft>(labeled.Body.Regions[0]);
        Assert.Equal(EndFinally.Index,
            Assert.IsType<StructuredLabelBranchDraft>(Assert.Single(conditional.WhenTrue.Regions)).TargetBlock);
        Assert.Equal(EndFinally.Index, Assert.IsType<StructuredLabelBranchDraft>(labeled.Body.Regions[1]).TargetBlock);
        Assert.Same(Throw, Assert.IsType<StructuredBlockDraft>(labeled.Body.Regions[2]).Block);
        var moved = Assert.IsType<StructuredBlockDraft>(placed.Regions[1]);
        Assert.Same(EndFinally, moved.Block);
        Assert.True(moved.IsOriginal);
    }

    [Fact]
    public void AHandlerWithTwoEndFinallyBlocksKeepsItsStructure()
    {
        // Only one block's code can come last, so the validator rejects this one with its own message.
        var handler = Sequence(
            new StructuredIfDraft(Condition,
                Sequence(new StructuredBlockDraft(OtherEndFinally)),
                StructuredSequenceDraft.Empty),
            new StructuredBlockDraft(EndFinally),
            new StructuredBlockDraft(Throw));

        Assert.Same(handler, EndFinallyPlacement.EndLast(handler));
    }

    private static StructuredSequenceDraft Sequence(params StructuredRegionDraft[] regions) => new([.. regions]);

    private static BasicBlock Block(int index, int offset, CilOperation terminator) => new(index, offset,
        [new CilInstruction(offset, offset + 1, terminator, new CilOperand.None())]);
}
