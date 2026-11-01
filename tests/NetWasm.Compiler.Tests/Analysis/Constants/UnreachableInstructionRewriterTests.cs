using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.Constants;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Analysis.Constants;

using static ConstantRewriteTestSupport;

public sealed class UnreachableInstructionRewriterTests
{
    [Fact]
    public void RejectsMissingGraphBuilderAndBody()
    {
        Assert.Throws<ArgumentNullException>(() => new UnreachableInstructionRewriter(null!));
        var body = Body(Instruction(0, CilOperation.Return));
        Assert.Throws<ArgumentNullException>(() => new UnreachableInstructionRewriter(Graph(body)).Rewrite(null!));
    }

    [Fact]
    public void KeepsDeadBranchTargetsAndTerminalInstructions()
    {
        var body = Body(
            Instruction(0, CilOperation.Branch, new CilOperand.BranchTarget(7)),
            Instruction(1, CilOperation.Nop),
            Instruction(2, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            Instruction(3, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(6)),
            Instruction(4, CilOperation.Branch, new CilOperand.BranchTarget(6)),
            Instruction(5, CilOperation.Throw),
            Instruction(6, CilOperation.Return),
            Instruction(7, CilOperation.Return));

        var rewritten = new UnreachableInstructionRewriter(Graph(body)).Rewrite(body);

        Assert.Equal(CilOperation.Nop, rewritten.Instructions[2].Operation);
        foreach (var index in new[] { 0, 1, 3, 4, 5, 6, 7 })
            Assert.Same(body.Instructions[index], rewritten.Instructions[index]);
    }

    [Fact]
    public void RemovesDeadDependenciesButPreservesControlFlow()
    {
        var body = Body(
            Instruction(0, CilOperation.Branch, new CilOperand.BranchTarget(4)),
            Instruction(1, CilOperation.LoadString, new CilOperand.None()),
            Instruction(2, CilOperation.Call, new CilOperand.MethodInstance(Method())),
            Instruction(3, CilOperation.Return),
            Instruction(4, CilOperation.Return));

        var rewritten = new UnreachableInstructionRewriter(Graph(body)).Rewrite(body);

        Assert.Equal(CilOperation.Nop, rewritten.Instructions[1].Operation);
        Assert.Equal(CilOperation.Nop, rewritten.Instructions[2].Operation);
        Assert.Equal(body.Instructions[0], rewritten.Instructions[0]);
        Assert.Equal(body.Instructions[3], rewritten.Instructions[3]);
        Assert.Equal(body.Instructions[4], rewritten.Instructions[4]);
    }

    [Fact]
    public void PreservesHandlersAndReachableDependencies()
    {
        var body = Body(
            Instruction(0, CilOperation.Call, new CilOperand.MethodInstance(Method())),
            Instruction(1, CilOperation.Leave, new CilOperand.BranchTarget(4)),
            Instruction(2, CilOperation.Call, new CilOperand.MethodInstance(Method())),
            Instruction(3, CilOperation.EndFinally),
            Instruction(4, CilOperation.Return)) with
        {
            ExceptionRegions = [new(CilExceptionRegionKind.Finally, 0, 2, 2, 2, null, null)],
        };

        Assert.Same(body, new UnreachableInstructionRewriter(Graph(body)).Rewrite(body));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreservesUnreachableExceptionMetadataForLexicalTransferValidation(bool hasDependency)
    {
        var body = Body(
            Instruction(0, CilOperation.Branch, new CilOperand.BranchTarget(5)),
            hasDependency
                ? Instruction(1, CilOperation.Call, new CilOperand.MethodInstance(Method()))
                : Instruction(1, CilOperation.Nop),
            Instruction(2, CilOperation.Leave, new CilOperand.BranchTarget(5)),
            Instruction(3, CilOperation.Nop),
            Instruction(4, CilOperation.EndFinally),
            Instruction(5, CilOperation.Return)) with
        {
            ExceptionRegions = [new(CilExceptionRegionKind.Finally, 1, 2, 3, 2, null, null)],
        };
        var rule = Assert.IsAssignableFrom<IMethodRewriteRule>(new UnreachableInstructionRewriter(Graph(body)));

        var rewritten = rule.Rewrite(body);

        Assert.Equal(body.ExceptionRegions, rewritten.ExceptionRegions);
        Assert.Equal(CilOperation.Nop, rewritten.Instructions[1].Operation);
        Assert.Equal(body.Instructions[0], rewritten.Instructions[0]);
        Assert.Equal(body.Instructions[2], rewritten.Instructions[2]);
        Assert.Equal(body.Instructions[4], rewritten.Instructions[4]);
        Assert.Equal(body.Instructions[5], rewritten.Instructions[5]);
    }
}
