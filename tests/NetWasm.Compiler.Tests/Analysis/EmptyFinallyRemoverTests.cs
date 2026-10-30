using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Analysis;

public sealed class EmptyFinallyRemoverTests
{
    private static readonly AssemblyIdentity Assembly = new("EmptyFinallyTests");

    // int x; try { x = 1; } finally { } return x;
    [Theory]
    [InlineData(CilExceptionRegionKind.Finally)]
    [InlineData(CilExceptionRegionKind.Fault)]
    public void RemovesAnEmptyHandlerAndBranchesWhereItsTryLeft(CilExceptionRegionKind kind)
    {
        var body = Body(
            [Region(kind, 1, 3, 4, 2)],
            I(0, CilOperation.Nop),
            I(1, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(2, CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(3, CilOperation.Leave, new CilOperand.BranchTarget(6)),
            I(4, CilOperation.Nop),
            I(5, CilOperation.EndFinally),
            I(6, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(7, CilOperation.Return));

        var result = new EmptyFinallyRemover(true).Rewrite(body);

        Assert.Empty(result.ExceptionRegions);
        Assert.Equal(I(3, CilOperation.Branch, new CilOperand.BranchTarget(6)), result.Instructions[3]);
        Assert.Equal(I(5, CilOperation.Nop), result.Instructions[5]);
        Assert.Equal(
            body.Instructions.Where((_, index) => index is not (3 or 5)),
            result.Instructions.Where((_, index) => index is not (3 or 5)));
    }

    // try { x = 1; } finally { x = 2; }
    [Fact]
    public void KeepsAHandlerThatRunsCode()
    {
        var body = Body(
            [Region(CilExceptionRegionKind.Finally, 0, 3, 3, 3)],
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(1, CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(2, CilOperation.Leave, new CilOperand.BranchTarget(6)),
            I(3, CilOperation.LoadInt32, new CilOperand.ConstantI4(2)),
            I(4, CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(5, CilOperation.EndFinally),
            I(6, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(7, CilOperation.Return));

        Assert.Same(body, new EmptyFinallyRemover(true).Rewrite(body));
    }

    // try { try { x = 1; } catch { x = 2; } } finally { }: both leaves still
    // leave the inner try or its catch, so they stay leaves.
    [Fact]
    public void KeepsTheLeavesThatStillLeaveAnotherRegion()
    {
        var @catch = new CilExceptionRegion(CilExceptionRegionKind.Catch, 2, 3, 5, 4, null, null);
        var body = Body(
            [@catch, Region(CilExceptionRegionKind.Finally, 2, 7, 9, 1)],
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(1, CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(2, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(3, CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(4, CilOperation.Leave, new CilOperand.BranchTarget(10)),
            I(5, CilOperation.Pop),
            I(6, CilOperation.LoadInt32, new CilOperand.ConstantI4(2)),
            I(7, CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(8, CilOperation.Leave, new CilOperand.BranchTarget(10)),
            I(9, CilOperation.EndFinally),
            I(10, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(11, CilOperation.Return));

        var result = new EmptyFinallyRemover(true).Rewrite(body);

        Assert.Same(@catch, Assert.Single(result.ExceptionRegions));
        Assert.Equal(CilOperation.Leave, result.Instructions[4].Operation);
        Assert.Equal(CilOperation.Leave, result.Instructions[8].Operation);
        Assert.Equal(CilOperation.Nop, result.Instructions[9].Operation);
    }

    // try { throw null; } finally { } with nothing after the handler: its nops
    // would run off the end of the method.
    [Fact]
    public void KeepsAHandlerThatEndsTheMethod()
    {
        var body = Body(
            [Region(CilExceptionRegionKind.Finally, 0, 2, 2, 1)],
            I(0, CilOperation.LoadNull),
            I(1, CilOperation.Throw),
            I(2, CilOperation.EndFinally));

        Assert.Same(body, new EmptyFinallyRemover(true).Rewrite(body));
    }

    [Fact]
    public void DisabledPolicyLeavesTheBody()
    {
        var body = Body(
            [Region(CilExceptionRegionKind.Finally, 0, 1, 1, 1)],
            I(0, CilOperation.Leave, new CilOperand.BranchTarget(2)),
            I(1, CilOperation.EndFinally),
            I(2, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(3, CilOperation.Return));

        Assert.Same(body, new EmptyFinallyRemover(false).Rewrite(body));
    }

    private static CilExceptionRegion Region(
        CilExceptionRegionKind kind, int tryOffset, int tryLength, int handlerOffset, int handlerLength) =>
        new(kind, tryOffset, tryLength, handlerOffset, handlerLength, null, null);

    private static CilInstruction I(int offset, CilOperation operation, CilOperand? operand = null) =>
        new(offset, offset + 1, operation, operand ?? new CilOperand.None());

    private static CilMethodBody Body(CilExceptionRegion[] regions, params CilInstruction[] instructions) =>
        new(new MethodDefinitionModel(new(Assembly, 0x06000001), new(Assembly, 0x02000001), "Run", true,
                MethodSignatureModel.Create(CliValueKind.I4), 0),
            2, [CliValueKind.I4], [.. instructions])
        {
            ExceptionRegions = [.. regions],
        };
}
