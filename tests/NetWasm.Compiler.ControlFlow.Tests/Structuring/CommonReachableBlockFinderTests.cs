// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.ControlFlow.Structuring;
using static NetWasm.Compiler.ControlFlow.Tests.ControlFlowTestSupport;

namespace NetWasm.Compiler.ControlFlow.Tests.Structuring;

public sealed class CommonReachableBlockFinderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MultipleStartsRequireAJoinThatPostDominatesEveryStart(bool reverse)
    {
        var state = new ControlFlowStructuringState(Validate(Body(
            CliValueKind.Void, 0, [], I(0, CilOperation.Return, new CilOperand.None()))));
        var actor = new CommonReachableBlockFinder(
            new PostDominators(), new Distances());

        var result = ((ICommonReachableBlockFinder)actor).Find(state, reverse ? [2, 1] : [1, 2], null, [1, 2, 3, 4]);

        Assert.Equal(4, result);
    }

    // A nested handler's leave out of an enclosing handler starts outside the blocks allowed: no join in them, as
    // for two starts (issue #259's sibling, a catch in a catch's loop that returns from the method).
    [Fact]
    public void AStartOutsideTheAllowedBlocksHasNoJoin()
    {
        var state = new ControlFlowStructuringState(Validate(Body(
            CliValueKind.Void, 0, [], I(0, CilOperation.Return, new CilOperand.None()))));
        var actor = new CommonReachableBlockFinder(new PostDominators(), new Distances());

        Assert.Null(actor.Find(state, [1, 2, 12], null, [1, 2, 3, 4]));
        Assert.Null(actor.Find(state, 1, 12, null, [1, 2, 3, 4]));
    }

    // Block 1 is post-dominated by 3 and 4, block 2 by 4.
    private sealed class PostDominators : IControlFlowPostDominatorFinder
    {
        public DominatorTree Find(ControlFlowStructuringState state,
            int? stop, ImmutableHashSet<int> allowed) => DominatorTree.FromImmediateDominators([1, 2, 3, 4],
            new Dictionary<int, int?> { [1] = 3, [2] = 4, [3] = 4, [4] = null });
    }

    private sealed class Distances : IControlFlowDistanceFinder
    {
        public Dictionary<int, int> Find(ControlFlowStructuringState state, int start,
            int? stop, ImmutableHashSet<int> allowed) => new()
            {
                [start] = 0,
                [3] = 1,
                [4] = 2,
            };
    }
}
