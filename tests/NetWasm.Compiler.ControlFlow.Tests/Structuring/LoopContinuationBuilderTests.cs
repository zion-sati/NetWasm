// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Draft;
using NetWasm.Compiler.ControlFlow.Structuring;

namespace NetWasm.Compiler.ControlFlow.Tests.Structuring;

// A protected block's continuation inside a loop body continues at the loop's continue block.
public sealed class LoopContinuationBuilderTests
{
    // Header 5 tests the condition; 9 is the latch every path back to 5 passes through; 10 is the exit.
    private static readonly LoopRegion Loop = new(5, 5, 9, 10, 10, [5, 6, 7, 8, 9], [6, 7, 8, 9], []);

    [Fact]
    public void AContinuationAtTheContinueBlockIsALoopContinue()
    {
        var state = ControlFlowStructuringStateTestFactory.Create();
        var ranges = new RecordingBlockRangeStructurer();

        var body = new LoopContinuationBuilder(ranges).Build(state, 9, 1, Loop);

        Assert.IsType<StructuredLoopContinueDraft>(Assert.Single(body.Regions));
        Assert.Null(ranges.Continue);
    }

    // Structured with the header as its continue target, a continuation reaching the latch ran it and then continued,
    // which ran it again: a for loop's increment twice.
    [Fact]
    public void AContinuationContinuesAtTheLatchAndBreaksToTheExit()
    {
        var state = ControlFlowStructuringStateTestFactory.Create();
        var ranges = new RecordingBlockRangeStructurer();

        new LoopContinuationBuilder(ranges).Build(state, 7, 2, Loop);

        Assert.Equal(9, ranges.Continue);
        Assert.Equal(10, ranges.Exit);
        Assert.Empty(state.LoopContinueTargets);
        Assert.Empty(state.LoopExitTargets);
    }

    private sealed class RecordingBlockRangeStructurer : IBlockRangeStructurer
    {
        public int? Continue { get; private set; }

        public int? Exit { get; private set; }

        public StructuredSequenceDraft Structure(ControlFlowStructuringState state, int offset, int length)
        {
            Continue = state.LoopContinueTargets.Peek();
            Exit = state.LoopExitTargets.Peek();
            return StructuredSequenceDraft.Empty;
        }
    }
}
