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
