using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using static NetWasm.Compiler.ControlFlow.Tests.ControlFlowTestSupport;

namespace NetWasm.Compiler.ControlFlow.Tests.Structuring;

public sealed class ExceptionContinuationLoopTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CatchOnlyBackedgeProducesOneLoopAroundTheProtectedRegion(bool filter)
    {
        var method = Structurize(filter ? FilterOnlyLoop() : CatchOnlyLoop());

        var loop = Assert.Single(method.Body.Regions.OfType<StructuredLoop>());
        var region = Assert.Single(loop.Body.Regions.OfType<StructuredExceptionRegion>());
        var group = method.ExceptionGroups[region.Group];
        Assert.Single(method.ExceptionGroups);
        Assert.Equal(0, region.FallthroughContinuation!.Value.Value);
        Assert.Empty(group.NormalContinuations[0].Body.Regions);
        Assert.Single(group.NormalContinuations[1].Body.Regions);
    }

    [Fact]
    public void CatchOnlyBackedgeRunsNestedFinallyBeforeContinuingTheLoop()
    {
        var body = Body(CliValueKind.Void, 1, [],
            I(0, CilOperation.Branch, new CilOperand.BranchTarget(14)),
            I(1, CilOperation.Nop),
            I(2, CilOperation.Call, new CilOperand.Entity(NoArgumentStaticCallKey)),
            I(3, CilOperation.Pop),
            I(4, CilOperation.Leave, new CilOperand.BranchTarget(17)),
            I(5, CilOperation.Pop),
            I(6, CilOperation.Nop),
            I(7, CilOperation.Call, new CilOperand.Entity(NoArgumentStaticCallKey)),
            I(8, CilOperation.Pop),
            I(9, CilOperation.Leave, new CilOperand.BranchTarget(11)),
            I(10, CilOperation.EndFinally),
            I(11, CilOperation.Leave, new CilOperand.BranchTarget(12)),
            I(12, CilOperation.Nop),
            I(13, CilOperation.Branch, new CilOperand.BranchTarget(14)),
            I(14, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(15, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(1)),
            I(16, CilOperation.Return),
            I(17, CilOperation.Return)) with
        {
            ExceptionRegions =
            [new(CilExceptionRegionKind.Finally, 7, 3, 10, 1, null, null),
             new(CilExceptionRegionKind.Catch, 2, 3, 5, 7, TypeKey, null)],
        };

        var method = Structurize(body);

        var loop = Assert.Single(method.Body.Regions.OfType<StructuredLoop>());
        var region = Assert.Single(loop.Body.Regions.OfType<StructuredExceptionRegion>());
        var group = method.ExceptionGroups[region.Group];
        var handler = Assert.Single(group.Clauses);
        var cleanup = Assert.Single(handler.HandlerBody.Regions.OfType<StructuredExceptionRegion>());
        Assert.Equal(CilExceptionRegionKind.Finally,
            Assert.Single(method.ExceptionGroups[cleanup.Group].Clauses).Kind);
        Assert.Equal(2, method.ExceptionGroups.Count);
        Assert.Empty(group.NormalContinuations[0].Body.Regions);
    }

    private static CilMethodBody FilterOnlyLoop() => Body(
        CliValueKind.Void, 1, [],
        I(0, CilOperation.Branch, new CilOperand.BranchTarget(12)),
        I(1, CilOperation.Nop),
        I(2, CilOperation.Call, new CilOperand.Entity(NoArgumentStaticCallKey)),
        I(3, CilOperation.Pop),
        I(4, CilOperation.Leave, new CilOperand.BranchTarget(15)),
        I(5, CilOperation.Pop),
        I(6, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
        I(7, CilOperation.EndFilter),
        I(8, CilOperation.Pop),
        I(9, CilOperation.Leave, new CilOperand.BranchTarget(10)),
        I(10, CilOperation.Nop),
        I(11, CilOperation.Branch, new CilOperand.BranchTarget(12)),
        I(12, CilOperation.LoadArgument, new CilOperand.Index(0)),
        I(13, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(1)),
        I(14, CilOperation.Return),
        I(15, CilOperation.Return)) with
    {
        ExceptionRegions = [new(CilExceptionRegionKind.Filter, 2, 3, 8, 2, null, 5)],
    };

    internal static CilMethodBody CatchOnlyLoop() => Body(
        CliValueKind.Void,
        1,
        [],
        I(0, CilOperation.Branch, new CilOperand.BranchTarget(9)),
        I(1, CilOperation.Nop),
        I(2, CilOperation.Call, new CilOperand.Entity(NoArgumentStaticCallKey)),
        I(3, CilOperation.Pop),
        I(4, CilOperation.Leave, new CilOperand.BranchTarget(12)),
        I(5, CilOperation.Pop),
        I(6, CilOperation.Leave, new CilOperand.BranchTarget(7)),
        I(7, CilOperation.Nop),
        I(8, CilOperation.Branch, new CilOperand.BranchTarget(9)),
        I(9, CilOperation.LoadArgument, new CilOperand.Index(0)),
        I(10, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(1)),
        I(11, CilOperation.Return),
        I(12, CilOperation.Return)) with
    {
        ExceptionRegions = [new(CilExceptionRegionKind.Catch, 2, 3, 5, 2, TypeKey, null)],
    };
}
