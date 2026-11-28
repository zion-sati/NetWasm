// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Analysis.AddressTakenValues;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

// The paths into a join's store are the instruction falling into it and each unconditional branch to it, each right
// after the instruction that pushed its value; any other entry, a value the caller does not accept or a single path
// finds no join.
public sealed class StructLocalJoinFinderTests
{
    private readonly IStructLocalJoinFinder _finder =
        Assert.IsAssignableFrom<IStructLocalJoinFinder>(new StructLocalJoinFinder());

    // 0 ldarg; 1 brtrue 4; 2 ldloc 0; 3 br 5; 4 ldloc 1; 5 stloc 2 (the join); 6 ret
    private static ImmutableArray<CilInstruction> Joined(CilOperation entry = CilOperation.Branch) =>
    [
        I(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
        I(1, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(4)),
        I(2, CilOperation.LoadLocal, new CilOperand.Index(0)),
        I(3, entry, new CilOperand.BranchTarget(5)),
        I(4, CilOperation.LoadLocal, new CilOperand.Index(1)),
        I(5, CilOperation.StoreLocal, new CilOperand.Index(2)),
        I(6, CilOperation.Return, new CilOperand.None()),
    ];

    [Fact]
    public void FindsTheValueOfEachPathIntoTheJoin()
    {
        var sources = _finder.Find(Joined(), 5, static producer => producer is 2 or 4);

        Assert.NotNull(sources);
        Assert.Equal([4, 2], sources.Value.AsEnumerable());
    }

    [Fact]
    public void AConditionalBranchASwitchOrARejectedValueFindsNoJoin()
    {
        Assert.Null(_finder.Find(Joined(CilOperation.BranchIfFalse), 5, static _ => true));
        Assert.Null(_finder.Find(Joined(), 5, static producer => producer == 4));
        var switched = Joined().SetItem(0, I(0, CilOperation.Switch, new CilOperand.SwitchTargets([5])));
        Assert.Null(_finder.Find(switched, 5, static _ => true));
    }

    // `x = a ? b : c ? d : e` after inlining: the arm building b branches to the branch ending the arm building d, so
    // both values reach the join through it.
    // 0 ldarg; 1 brtrue 6; 2 ldarg; 3 brtrue 8; 4 ldloc 0; 5 br 7 (b's arm ends); 6 ldloc 1; 7 br 9 (d's arm
    // ends); 8 ldloc 2; 9 stloc 3 (the join); 10 ret
    [Fact]
    public void FindsTheValuesReachingABranchToTheJoin()
    {
        ImmutableArray<CilInstruction> chained =
        [
            I(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(1, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(6)),
            I(2, CilOperation.LoadArgument, new CilOperand.Index(1)),
            I(3, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(8)),
            I(4, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(5, CilOperation.Branch, new CilOperand.BranchTarget(7)),
            I(6, CilOperation.LoadLocal, new CilOperand.Index(1)),
            I(7, CilOperation.Branch, new CilOperand.BranchTarget(9)),
            I(8, CilOperation.LoadLocal, new CilOperand.Index(2)),
            I(9, CilOperation.StoreLocal, new CilOperand.Index(3)),
            I(10, CilOperation.Return, new CilOperand.None()),
        ];

        var sources = _finder.Find(chained, 9, static producer => producer is 4 or 6 or 8);

        Assert.NotNull(sources);
        Assert.Equal([8, 6, 4], sources.Value.AsEnumerable());
        // A value reaching the inner branch that the caller rejects, or a conditional branch into it, finds no join.
        Assert.Null(_finder.Find(chained, 9, static producer => producer is 6 or 8));
        Assert.Null(_finder.Find(chained.SetItem(5, I(5, CilOperation.BranchIfFalse, new CilOperand.BranchTarget(7))),
            9, static _ => true));
        // A branch to the join that no path reaches finds none either.
        var unreached = chained.SetItem(6, I(6, CilOperation.Return, new CilOperand.None()))
            .SetItem(5, I(5, CilOperation.Branch, new CilOperand.BranchTarget(10)));
        Assert.Null(_finder.Find(unreached, 9, static _ => true));
    }

    // A join with one path is a plain store, which copies already cover.
    [Fact]
    public void OnePathFindsNoJoin()
    {
        var single = Joined().SetItem(3, I(3, CilOperation.Branch, new CilOperand.BranchTarget(6)));

        Assert.Null(_finder.Find(single, 5, static _ => true));
        Assert.Throws<System.ArgumentOutOfRangeException>(() => _finder.Find(Joined(), 7, static _ => true));
        Assert.Throws<System.ArgumentNullException>(() => _finder.Find(Joined(), 5, null!));
    }

    private static CilInstruction I(int offset, CilOperation operation, CilOperand operand) =>
        new(offset, offset + 1, operation, operand);
}
