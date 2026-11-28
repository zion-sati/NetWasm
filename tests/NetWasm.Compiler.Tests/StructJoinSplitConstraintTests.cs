// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Analysis.AddressTakenValues;

namespace NetWasm.Compiler.Tests;

// A join's target splits only with every source split, through chains of joins; a source keeps its own choice.
public sealed class StructJoinSplitConstraintTests
{
    private readonly IStructJoinSplitConstraint _constraint =
        Assert.IsAssignableFrom<IStructJoinSplitConstraint>(new StructJoinSplitConstraint());

    [Fact]
    public void ATargetWithAWholeSourceStaysWholeThroughAChain()
    {
        // 2 joins 0 and 1; 3 joins 2 and 4.
        var joins = new[] { (2, 0), (2, 1), (3, 2), (3, 4) }.ToLookup(static pair => pair.Item1, static pair => pair.Item2);
        var split = new HashSet<int> { 0, 2, 3, 4 };

        _constraint.Apply(split, joins);

        Assert.Equal([0, 4], split.Order());
    }

    [Fact]
    public void ATargetWithEverySourceSplitStaysSplit()
    {
        var joins = new[] { (2, 0), (2, 1) }.ToLookup(static pair => pair.Item1, static pair => pair.Item2);
        var split = new HashSet<int> { 0, 1, 2 };

        _constraint.Apply(split, joins);

        Assert.Equal([0, 1, 2], split.Order());
        Assert.Throws<System.ArgumentNullException>(() => _constraint.Apply(null!, joins));
        Assert.Throws<System.ArgumentNullException>(() => _constraint.Apply(split, null!));
    }
}
