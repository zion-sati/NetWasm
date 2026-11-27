// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class CilLeaveChainerTests
{
    // try { try { nop; leave after } catch { pop; leave after } } finally { endfinally } after: ret
    // The inner block's leaves exit the inner try (or its handler) and the outer try: each now leaves to a leave at
    // the end of the outer try, which leaves to `after`; the outer try grows to hold it.
    private static readonly ImmutableArray<CilInstruction> Nested =
    [
        new(0, 1, CilOperation.Nop, new CilOperand.None()),
        new(1, 2, CilOperation.Leave, new CilOperand.BranchTarget(5)),
        new(2, 3, CilOperation.Pop, new CilOperand.None()),
        new(3, 4, CilOperation.Leave, new CilOperand.BranchTarget(5)),
        new(4, 5, CilOperation.EndFinally, new CilOperand.None()),
        new(5, 6, CilOperation.Return, new CilOperand.None()),
    ];

    private static readonly ImmutableArray<CilExceptionRegion> NestedRegions =
    [
        new(CilExceptionRegionKind.Catch, 0, 2, 2, 2, null, null),
        new(CilExceptionRegionKind.Finally, 0, 4, 4, 1, null, null),
    ];

    [Fact]
    public void ALeaveOutOfTwoBlocksLeavesOneAtATime()
    {
        var chained = ((ICilLeaveChainer)new CilLeaveChainer()).Chain(Nested, NestedRegions);

        Assert.True(chained.Changed);
        // nop, leave, pop, leave, (added) leave, endfinally, ret
        Assert.Equal(
            [CilOperation.Nop, CilOperation.Leave, CilOperation.Pop, CilOperation.Leave, CilOperation.Leave,
                CilOperation.EndFinally, CilOperation.Return],
            chained.Instructions.Select(instruction => instruction.Operation));
        Assert.Equal(4, Target(chained.Instructions[1]));
        Assert.Equal(4, Target(chained.Instructions[3]));
        Assert.Equal(6, Target(chained.Instructions[4]));
        var inner = chained.ExceptionRegions[0];
        var outer = chained.ExceptionRegions[1];
        Assert.Equal((0, 2, 2, 2), (inner.TryOffset, inner.TryLength, inner.HandlerOffset, inner.HandlerLength));
        // The added leave is in the outer try and outside the inner try and its handler.
        Assert.Equal((0, 5, 5, 1), (outer.TryOffset, outer.TryLength, outer.HandlerOffset, outer.HandlerLength));
        // The added leave reports the first leave that passes through it.
        Assert.Equal(1, chained.Instructions[4].SourceOffset);
    }

    [Fact]
    public void ALeaveOutOfOneBlockIsLeftAlone()
    {
        ImmutableArray<CilInstruction> instructions =
        [
            new(0, 1, CilOperation.Nop, new CilOperand.None()),
            new(1, 2, CilOperation.Leave, new CilOperand.BranchTarget(3)),
            new(2, 3, CilOperation.EndFinally, new CilOperand.None()),
            new(3, 4, CilOperation.Return, new CilOperand.None()),
        ];
        ImmutableArray<CilExceptionRegion> regions = [new(CilExceptionRegionKind.Finally, 0, 2, 2, 1, null, null)];

        var chained = ((ICilLeaveChainer)new CilLeaveChainer()).Chain(instructions, regions);

        Assert.False(chained.Changed);
        Assert.Equal(instructions, chained.Instructions);
        Assert.Equal(regions, chained.ExceptionRegions);
    }

    [Fact]
    public void ABlockThatStartsWhereAnotherEndsStartsAfterTheAddedLeave()
    {
        // The nested pair, then a second try/finally starting where the first outer try's handler ends.
        ImmutableArray<CilInstruction> instructions =
        [
            .. Nested.SkipLast(1),
            new(5, 6, CilOperation.Nop, new CilOperand.None()),
            new(6, 7, CilOperation.Leave, new CilOperand.BranchTarget(8)),
            new(7, 8, CilOperation.EndFinally, new CilOperand.None()),
            new(8, 9, CilOperation.Return, new CilOperand.None()),
        ];
        ImmutableArray<CilExceptionRegion> regions =
        [
            .. NestedRegions,
            new(CilExceptionRegionKind.Finally, 5, 2, 7, 1, null, null),
        ];

        var chained = ((ICilLeaveChainer)new CilLeaveChainer()).Chain(instructions, regions);

        var second = chained.ExceptionRegions[2];
        Assert.Equal(6, second.TryOffset);
        Assert.Equal(CilOperation.Nop, chained.Instructions[second.TryOffset].Operation);
        Assert.Equal(6, Target(chained.Instructions[4]));
    }

    [Fact]
    public void ABlockLeftForTwoTargetsKeepsItsLeaves()
    {
        // try { try { leave a } catch { pop; leave b } } finally { endfinally } a: ret b: ret
        // The outer try is left for both a and b: a nested group with two continuations has none to dispatch between
        // them, so neither leave is chained.
        ImmutableArray<CilInstruction> instructions =
        [
            new(0, 1, CilOperation.Leave, new CilOperand.BranchTarget(4)),
            new(1, 2, CilOperation.Pop, new CilOperand.None()),
            new(2, 3, CilOperation.Leave, new CilOperand.BranchTarget(5)),
            new(3, 4, CilOperation.EndFinally, new CilOperand.None()),
            new(4, 5, CilOperation.Return, new CilOperand.None()),
            new(5, 6, CilOperation.Return, new CilOperand.None()),
        ];
        ImmutableArray<CilExceptionRegion> regions =
        [
            new(CilExceptionRegionKind.Catch, 0, 1, 1, 2, null, null),
            new(CilExceptionRegionKind.Finally, 0, 3, 3, 1, null, null),
        ];

        var chained = ((ICilLeaveChainer)new CilLeaveChainer()).Chain(instructions, regions);

        Assert.False(chained.Changed);
        Assert.Equal(instructions, chained.Instructions);
    }

    [Fact]
    public void ALeaveThatWouldHopOverALaterBlockKeepsItsTarget()
    {
        // try { try { leave after } catch { pop; leave after } try { nop; leave next } finally { endfinally }
        //   next: leave after } finally { endfinally } after: ret
        // A hop to the end of the outer try would pass the second inner block, beyond the first one's continuation.
        ImmutableArray<CilInstruction> instructions =
        [
            new(0, 1, CilOperation.Leave, new CilOperand.BranchTarget(8)),
            new(1, 2, CilOperation.Pop, new CilOperand.None()),
            new(2, 3, CilOperation.Leave, new CilOperand.BranchTarget(8)),
            new(3, 4, CilOperation.Nop, new CilOperand.None()),
            new(4, 5, CilOperation.Leave, new CilOperand.BranchTarget(6)),
            new(5, 6, CilOperation.EndFinally, new CilOperand.None()),
            new(6, 7, CilOperation.Leave, new CilOperand.BranchTarget(8)),
            new(7, 8, CilOperation.EndFinally, new CilOperand.None()),
            new(8, 9, CilOperation.Return, new CilOperand.None()),
        ];
        ImmutableArray<CilExceptionRegion> regions =
        [
            new(CilExceptionRegionKind.Catch, 0, 1, 1, 2, null, null),
            new(CilExceptionRegionKind.Finally, 3, 2, 5, 1, null, null),
            new(CilExceptionRegionKind.Finally, 0, 7, 7, 1, null, null),
        ];

        var chained = ((ICilLeaveChainer)new CilLeaveChainer()).Chain(instructions, regions);

        Assert.False(chained.Changed);
        Assert.Equal(instructions, chained.Instructions);
    }

    private static int Target(CilInstruction instruction) =>
        Assert.IsType<CilOperand.BranchTarget>(instruction.Operand).Offset;
}
