// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Wasm.Emission.Values.AggregateAnalysis;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class StructuredLoopDepthMeasurerTests
{
    // A switch's head and chain sit at the depth of the loops around it.
    [Fact]
    public void ASwitchsBlocksTakeTheLoopsDepth()
    {
        static StructuredBlockOccurrence Block(int index) => new(new StructuredBlockId(index), StructuredBlockRole.Owner);
        var table = new StructuredSwitch(Block(1), [Block(2), Block(3)], [new(4)], new(4));
        var loop = new StructuredLoop(null, true, new([table]), StructuredSequence.Empty, StructuredSequence.Empty);

        var depths = new StructuredLoopDepthMeasurer().Measure(new([new StructuredCode(Block(0)), loop]));

        Assert.Equal(0, depths[new(0)]);
        Assert.Equal([1, 1, 1], [depths[new(1)], depths[new(2)], depths[new(3)]]);
    }
}
