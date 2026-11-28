// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structuring;
using Xunit;

namespace NetWasm.Compiler.ControlFlow.Tests.Structuring;

// Post-dominator trees given by each block's immediate post-dominator; the targets are blocks 1 to 6.
public sealed class SwitchJoinSelectorTests
{
    // Every case assigns and meets at the code after the switch (20), which returns through 21.
    [Fact]
    public void CasesThatMeetJoinThere()
    {
        var joins = Select(new() { [1] = 20, [2] = 20, [3] = 20, [20] = 21, [21] = null }, [1, 2, 3]);

        Assert.Equal(new SwitchMeeting(20, 20), joins.Meeting);
        Assert.All(joins.Destinations.Values, destination => Assert.Equal(20, destination));
    }

    // In a loop: breaks reach the code after the switch (20), then the increment (21); a continue skips to the
    // increment, directly (3 is the increment) or through a block of its own (4); 5 leaves for the loop's exit (30),
    // a terminal, and 6 returns.
    [Fact]
    public void AContinueSkipsTheCodeAfterTheSwitchForTheOuterJoin()
    {
        var joins = Select(
            new() { [1] = 20, [2] = 20, [4] = 21, [5] = 30, [6] = null, [20] = 21, [21] = null, [30] = null },
            [1, 2, 21, 4, 5, 6],
            terminals: [30]);

        Assert.Equal(new SwitchMeeting(21, 20), joins.Meeting);
        Assert.Equal(20, joins.Destinations[1]);
        Assert.Equal(20, joins.Destinations[2]);
        Assert.Equal(21, joins.Destinations[21]);
        Assert.Equal(21, joins.Destinations[4]);
        Assert.Null(joins.Destinations[5]);
        Assert.Null(joins.Destinations[6]);
    }

    // Cases that only return, or end at the loop's continue (10), meet nowhere.
    [Fact]
    public void CasesThatEndOnTheirOwnHaveNoJoin()
    {
        var joins = Select(new() { [1] = null, [2] = 10, [3] = 10, [10] = null }, [1, 2, 3], terminals: [10]);

        Assert.Null(joins.Meeting);
        Assert.All(joins.Destinations.Values, destination => Assert.Null(destination));
    }

    // Two cases meet at 20, which returns; two others at 22, which returns too. The join is where most meet, the
    // nearer of two blocks as many reach, and then the lower; a target the tree leaves out (the enclosing join, 40) is
    // its own.
    [Fact]
    public void TheJoinIsWhereMostMeetAndNearest()
    {
        var joins = Select(
            new() { [1] = 20, [2] = 20, [3] = 20, [4] = 22, [5] = 22, [20] = 21, [21] = null, [22] = null },
            [1, 2, 3, 4, 5, 40]);
        var tied = Select(new() { [1] = 20, [2] = 20, [3] = 22, [4] = 22, [20] = null, [22] = null }, [1, 2, 3, 4]);

        Assert.Equal(new SwitchMeeting(20, 20), joins.Meeting);
        Assert.Null(joins.Destinations[4]);
        Assert.Null(joins.Destinations[40]);
        Assert.Equal(new SwitchMeeting(20, 20), tied.Meeting);
    }

    // A table of thousands of targets ahead of tens of thousands of blocks: each block is visited once, where walking
    // every target's post-dominators to the root held a copy of the path per target and compared the shared blocks
    // pairwise.
    [Fact]
    public void ManyTargetsAheadOfManyBlocksMeetAtTheFirst()
    {
        const int targets = 4096;
        const int blocks = 50_000;
        var immediate = new Dictionary<int, int?>();
        for (var target = 0; target < targets; target++) immediate[target] = targets;
        for (var block = targets; block < targets + blocks; block++) immediate[block] = block + 1 < targets + blocks ? block + 1 : null;
        var started = System.Diagnostics.Stopwatch.StartNew();

        var joins = Select(immediate, [.. Enumerable.Range(0, targets)]);

        Assert.Equal(new SwitchMeeting(targets, targets), joins.Meeting);
        Assert.All(joins.Destinations.Values, destination => Assert.Equal(targets, destination));
        Assert.InRange(started.Elapsed.TotalSeconds, 0, 10);
    }

    [Fact]
    public void SelectRejectsNullArguments()
    {
        var tree = DominatorTree.FromImmediateDominators([1], new Dictionary<int, int?> { [1] = null });
        var selector = new SwitchJoinSelector();

        Assert.Throws<ArgumentNullException>(() => selector.Select(null!, [1], []));
        Assert.Throws<ArgumentNullException>(() => selector.Select(tree, null!, []));
        Assert.Throws<ArgumentNullException>(() => selector.Select(tree, [1], null!));
    }

    private static SwitchJoins Select(Dictionary<int, int?> immediate, int[] targets, ImmutableHashSet<int>? terminals = null) =>
        new SwitchJoinSelector().Select(
            DominatorTree.FromImmediateDominators([.. immediate.Keys], immediate), targets, terminals ?? []);
}
