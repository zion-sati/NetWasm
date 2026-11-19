// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Analysis.Constants;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Analysis.Constants;

using static ConstantRewriteTestSupport;

public sealed class ConstantValueFlowTests
{
    // An inlined callee's argument, pushed before the bounds checks of the arguments after it, is stored to the
    // callee's parameter local in a later block: its loads still read the null.
    [Fact]
    public void AStackNullStoredInALaterBlockReachesItsLoads()
    {
        var body = Locals(
            Instruction(0, CilOperation.LoadNull),
            Instruction(1, CilOperation.LoadArgument, new CilOperand.Index(0)),
            Instruction(2, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(4)),
            Instruction(3, CilOperation.Nop),
            Instruction(4, CilOperation.StoreLocal, new CilOperand.Index(0)),
            Instruction(5, CilOperation.LoadLocal, new CilOperand.Index(0)),
            Instruction(6, CilOperation.Return));

        Assert.True(Solve(body).TryGetValue(5, out var value));
        Assert.Equal(KnownConstant.Null, value);
    }

    // A join keeps what every path agrees on: the same int32 from both, nothing when they differ or one stores an
    // unknown value.
    [Theory]
    [InlineData(7, 7, true)]
    [InlineData(7, 8, false)]
    [InlineData(7, null, false)]
    public void AJoinKnowsWhatEveryPathStored(int left, int? right, bool known)
    {
        var body = Locals(
            Instruction(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            Instruction(1, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(5)),
            Instruction(2, CilOperation.LoadInt32, new CilOperand.ConstantI4(left)),
            Instruction(3, CilOperation.StoreLocal, new CilOperand.Index(1)),
            Instruction(4, CilOperation.Branch, new CilOperand.BranchTarget(7)),
            right is { } value
                ? Instruction(5, CilOperation.LoadInt32, new CilOperand.ConstantI4(value))
                : Instruction(5, CilOperation.LoadArgument, new CilOperand.Index(1)),
            Instruction(6, CilOperation.StoreLocal, new CilOperand.Index(1)),
            Instruction(7, CilOperation.LoadLocal, new CilOperand.Index(1)),
            Instruction(8, CilOperation.Return));

        Assert.Equal(known, Solve(body).TryGetValue(7, out var loaded));
        if (known) Assert.Equal(KnownConstant.Int32(left), loaded);
    }

    // A call cannot write a local whose address is never taken; one whose address is taken is not followed at all.
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void ALocalOutlivesACallUnlessItsAddressIsTaken(bool addressTaken, bool known)
    {
        var body = Locals(
            Instruction(0, CilOperation.LoadNull),
            Instruction(1, CilOperation.StoreLocal, new CilOperand.Index(0)),
            Instruction(2, CilOperation.LoadNull),
            Instruction(3, CilOperation.LoadNull),
            Instruction(4, CilOperation.Call, new CilOperand.MethodInstance(Method())),
            Instruction(5, CilOperation.Pop),
            addressTaken
                ? Instruction(6, CilOperation.LoadLocalAddress, new CilOperand.Index(0))
                : Instruction(6, CilOperation.LoadNull),
            Instruction(7, CilOperation.Pop),
            Instruction(8, CilOperation.LoadLocal, new CilOperand.Index(0)),
            Instruction(9, CilOperation.Return));

        Assert.Equal(known, Solve(body).TryGetValue(8, out _));
    }

    // An instruction whose stack effect is unknown forgets the stack, so what it leaves is not stored as a constant;
    // the locals are kept.
    [Fact]
    public void AnUnknownStackEffectForgetsTheStackButNotTheLocals()
    {
        var body = Locals(
            Instruction(0, CilOperation.LoadNull),
            Instruction(1, CilOperation.StoreLocal, new CilOperand.Index(0)),
            Instruction(2, CilOperation.LoadNull),
            Instruction(3, CilOperation.CallIndirect),
            Instruction(4, CilOperation.LoadLocal, new CilOperand.Index(0)),
            Instruction(5, CilOperation.Pop),
            Instruction(6, CilOperation.StoreLocal, new CilOperand.Index(0)),
            Instruction(7, CilOperation.LoadLocal, new CilOperand.Index(0)),
            Instruction(8, CilOperation.Return));

        var facts = Solve(body);

        Assert.True(facts.TryGetValue(4, out var kept));
        Assert.Equal(KnownConstant.Null, kept);
        Assert.False(facts.TryGetValue(7, out _));
    }

    // A handler can start after any instruction of its try block, so it knows nothing; what a finally stores reaches
    // the code after it, which keeps only what both agree on.
    [Fact]
    public void AHandlerStartsKnowingNothingAndAFinallysStoreReachesTheCodeAfterIt()
    {
        var body = Locals(
            Instruction(0, CilOperation.LoadNull),
            Instruction(1, CilOperation.StoreLocal, new CilOperand.Index(0)),
            Instruction(2, CilOperation.Leave, new CilOperand.BranchTarget(7)),
            Instruction(3, CilOperation.LoadLocal, new CilOperand.Index(0)),
            Instruction(4, CilOperation.LoadArgument, new CilOperand.Index(0)),
            Instruction(5, CilOperation.StoreLocal, new CilOperand.Index(0)),
            Instruction(6, CilOperation.EndFinally),
            Instruction(7, CilOperation.LoadLocal, new CilOperand.Index(0)),
            Instruction(8, CilOperation.Return)) with
        {
            ExceptionRegions = [new CilExceptionRegion(CilExceptionRegionKind.Finally, 0, 3, 3, 4, null, null)],
        };

        var facts = Solve(body);

        Assert.False(facts.TryGetValue(3, out _));
        Assert.False(facts.TryGetValue(7, out _));
    }

    // The catch handler a filter leads to has no edge from endfilter in the graph: it still starts knowing nothing, and
    // what it stores reaches the join after the try (as a Debug build's return value does), which keeps neither value.
    [Fact]
    public void AFiltersHandlerStoreReachesTheJoinAfterTheTry()
    {
        var body = Locals(
            Instruction(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(6)),
            Instruction(1, CilOperation.StoreLocal, new CilOperand.Index(1)),
            Instruction(2, CilOperation.Leave, new CilOperand.BranchTarget(10)),
            Instruction(3, CilOperation.Pop),
            Instruction(4, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            Instruction(5, CilOperation.EndFilter),
            Instruction(6, CilOperation.Pop),
            Instruction(7, CilOperation.LoadInt32, new CilOperand.ConstantI4(100)),
            Instruction(8, CilOperation.StoreLocal, new CilOperand.Index(1)),
            Instruction(9, CilOperation.Leave, new CilOperand.BranchTarget(10)),
            Instruction(10, CilOperation.LoadLocal, new CilOperand.Index(1)),
            Instruction(11, CilOperation.Return)) with
        {
            ExceptionRegions = [new CilExceptionRegion(CilExceptionRegionKind.Filter, 0, 3, 6, 4, null, 3)],
        };

        Assert.False(Solve(body).TryGetValue(10, out _));
    }

    // A null stored to an int32 local, or an int32 to a reference local, is not a constant of that local.
    [Fact]
    public void FollowsOnlyInt32AndReferenceLocalsWithTheirOwnKindOfConstant()
    {
        var body = new CilMethodBody(Method().Definition, 4, [CliValueKind.I4, CliValueKind.I8], [
            Instruction(0, CilOperation.LoadNull),
            Instruction(1, CilOperation.StoreLocal, new CilOperand.Index(0)),
            Instruction(2, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            Instruction(3, CilOperation.StoreLocal, new CilOperand.Index(1)),
            Instruction(4, CilOperation.LoadLocal, new CilOperand.Index(0)),
            Instruction(5, CilOperation.LoadLocal, new CilOperand.Index(1)),
            Instruction(6, CilOperation.Return)]);

        var facts = Solve(body);

        Assert.False(facts.TryGetValue(4, out _));
        Assert.False(facts.TryGetValue(5, out _));
    }

    [Fact]
    public void RejectsMissingResolverAndGraph()
    {
        Assert.Throws<ArgumentNullException>(() => new ConstantValueFlow(null!));
        Assert.Throws<ArgumentNullException>(() => Flow().Solve(null!));
    }

    private static CilMethodBody Locals(params CilInstruction[] instructions) =>
        new(Method().Definition, 4, [CliValueKind.ManagedReference, CliValueKind.I4], [.. instructions]);

    private static ConstantValueFacts Solve(CilMethodBody body) =>
        Flow().Solve(new ControlFlowGraphBuilderFactory().Create().Build(body));
}
