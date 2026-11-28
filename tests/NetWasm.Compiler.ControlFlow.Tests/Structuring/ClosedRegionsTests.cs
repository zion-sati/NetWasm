// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structuring;
using Xunit;

namespace NetWasm.Compiler.ControlFlow.Tests.Structuring;

public sealed class ClosedRegionsTests
{
    // The reserved blocks are protected regions' entries, dispatchers' blocks, loop headers that are not suppressed, the
    // innermost active dispatcher's blocks and the terminals, asked of the state block by block.
    [Fact]
    public void ReservedBlocksAreTheStructuredOnesTheActiveDispatchersAndTheTerminals()
    {
        var state = ControlFlowStructuringStateTestFactory.Create();
        var loop = new LoopRegion(21, 21, 21, 23, 23, [21], [21], []);
        state.ExceptionGroupsByEntry[11] = null!;
        state.DispatchersByNode[12] = [12, 13];
        state.LoopsByHeader[21] = loop;
        state.LoopsByHeader[22] = loop with { Header = 22 };
        state.SuppressedLoopHeaders.Add(22);
        state.ActiveDispatchers.Push([101]);
        state.ActiveDispatchers.Push([102]);
        ImmutableHashSet<int> terminals = [99];

        var reserved = ClosedRegions.Reserved(state, terminals);

        ImmutableHashSet<int> expected = [11, 12, 21, 99, 102];
        for (var block = 0; block < 103; block++)
        {
            Assert.Equal(expected.Contains(block), reserved.Contains(block));
        }
    }

    [Fact]
    public void NoActiveDispatcherReservesNoneOfItsBlocks()
    {
        var state = ControlFlowStructuringStateTestFactory.Create();

        var reserved = ClosedRegions.Reserved(state, []);

        Assert.False(reserved.Contains(101));
        Assert.False(reserved.Contains(0));
    }
}
