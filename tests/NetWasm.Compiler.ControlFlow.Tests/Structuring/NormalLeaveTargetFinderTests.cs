using NetWasm.Compiler.ControlFlow.Structuring;
using NetWasm.Compiler.Core;
using static NetWasm.Compiler.ControlFlow.Tests.ControlFlowTestSupport;

namespace NetWasm.Compiler.ControlFlow.Tests.Structuring;

public sealed class NormalLeaveTargetFinderTests
{
    [Fact]
    public void UnreachableLeaveDoesNotCreateAContinuation()
    {
        var body = BodyWithUnreachableLeave();
        var state = new ControlFlowStructuringState(Validate(body));
        var finder = new NormalLeaveTargetFinder();

        Assert.Equal([5], ((INormalLeaveTargetFinder)finder).Find(
            state, body.ExceptionRegions, [], []));
    }

    [Fact]
    public void UnreachableLeaveDoesNotOwnADeadReturnEpilogue()
    {
        var method = Structurize(BodyWithUnreachableLeave());

        var group = Assert.Single(method.ExceptionGroups.Values);
        Assert.Equal(5, Assert.Single(group.NormalContinuations).TargetOffset);
        Assert.DoesNotContain(method.Blocks.Values, block => block.StartOffset == 7);
    }

    [Fact]
    public void AChildLeaveBackToALoopHeaderBeforeTheRegionLeavesTheRegionToo()
    {
        var body = UnconditionalLoopStructuringTests.NestedLeaveToHeader();
        var state = new ControlFlowStructuringState(Validate(body));
        var finder = new NormalLeaveTargetFinder();
        var inner = new ExceptionGroupSource(2, 4, [body.ExceptionRegions[0]]);

        Assert.Equal([0, 10], ((INormalLeaveTargetFinder)finder).Find(
            state, [body.ExceptionRegions[1]], [inner], []));
    }

    [Fact]
    public void ALeaveFromAGroupInACatchHandlerIsTheHandlersRegionsTargetOnlyWhenItLeavesTheHandler()
    {
        var body = BodyWithLeavesFromAFinallyInACatchHandler();
        var state = new ControlFlowStructuringState(Validate(body));
        var finder = new NormalLeaveTargetFinder();
        var inner = new ExceptionGroupSource(3, 4, [body.ExceptionRegions[0]]);

        // The handler's own leave goes to 9; the inner try's leave to 8 stays in the handler,
        // and its leave to 11 leaves the handler as well, so the catch region continues there.
        Assert.Equal([9, 11], ((INormalLeaveTargetFinder)finder).Find(
            state, [body.ExceptionRegions[1]], [inner], [inner]));
    }

    // A continue through a finally in a loop's catch handler: the inner try's backward leave to the loop
    // header, before the region, leaves the handler, so the catch region continues there too.
    [Fact]
    public void ABackwardLeaveFromAGroupInACatchHandlerIsTheHandlersRegionsTarget()
    {
        var body = BodyWithABackwardLeaveFromAFinallyInACatchHandler();
        var state = new ControlFlowStructuringState(Validate(body));
        var inner = new ExceptionGroupSource(3, 4, [body.ExceptionRegions[0]]);

        Assert.Equal([0, 10], ((INormalLeaveTargetFinder)new NormalLeaveTargetFinder()).Find(
            state, [body.ExceptionRegions[1]], [inner], [inner]));
    }

    private static CilMethodBody BodyWithABackwardLeaveFromAFinallyInACatchHandler() => Body(
        CliValueKind.I4, 1, [],
        I(0, CilOperation.Nop),
        I(1, CilOperation.Leave, new CilOperand.BranchTarget(10)),
        I(2, CilOperation.Pop),
        I(3, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
        I(4, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(6)),
        I(5, CilOperation.Leave, new CilOperand.BranchTarget(0)),
        I(6, CilOperation.Leave, new CilOperand.BranchTarget(8)),
        I(7, CilOperation.EndFinally),
        I(8, CilOperation.Leave, new CilOperand.BranchTarget(10)),
        I(9, CilOperation.Nop),
        I(10, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
        I(11, CilOperation.Return)) with
    {
        ExceptionRegions =
        [
            new(CilExceptionRegionKind.Finally, 3, 4, 7, 1, null, null),
            new(CilExceptionRegionKind.Catch, 1, 1, 2, 7, TypeKey, null),
        ],
    };

    private static CilMethodBody BodyWithLeavesFromAFinallyInACatchHandler() => Body(
        CliValueKind.I4, 1, [],
        I(0, CilOperation.Nop),
        I(1, CilOperation.Leave, new CilOperand.BranchTarget(9)),
        I(2, CilOperation.Pop),
        I(3, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
        I(4, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(6)),
        I(5, CilOperation.Leave, new CilOperand.BranchTarget(8)),
        I(6, CilOperation.Leave, new CilOperand.BranchTarget(11)),
        I(7, CilOperation.EndFinally),
        I(8, CilOperation.Leave, new CilOperand.BranchTarget(9)),
        I(9, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
        I(10, CilOperation.Return),
        I(11, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
        I(12, CilOperation.Return)) with
    {
        ExceptionRegions =
        [
            new(CilExceptionRegionKind.Finally, 3, 4, 7, 1, null, null),
            new(CilExceptionRegionKind.Catch, 0, 2, 2, 7, TypeKey, null),
        ],
    };

    private static CilMethodBody BodyWithUnreachableLeave() => Body(
        CliValueKind.I4, 1, [],
        I(0, CilOperation.LoadNull),
        I(1, CilOperation.Throw),
        I(2, CilOperation.Leave, new CilOperand.BranchTarget(7)),
        I(3, CilOperation.Pop),
        I(4, CilOperation.Leave, new CilOperand.BranchTarget(5)),
        I(5, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
        I(6, CilOperation.Return),
        I(7, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
        I(8, CilOperation.Return)) with
    {
        ExceptionRegions = [new(CilExceptionRegionKind.Catch, 0, 3, 3, 2, TypeKey, null)],
    };
}
