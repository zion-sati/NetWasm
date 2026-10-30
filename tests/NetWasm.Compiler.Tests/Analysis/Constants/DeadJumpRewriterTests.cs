using NetWasm.Compiler.Analysis.Constants;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Analysis.Constants;

using static ConstantRewriteTestSupport;

public sealed class DeadJumpRewriterTests
{
    // ldc 4; br 5; [dead: ldc 67; call; ldnull; throw]; 5: ldc 16; bgt ...
    // The jump and the dead throw fall through, so the two constants share a block.
    [Fact]
    public void JumpAcrossDeadCodeFallsThrough()
    {
        var body = Body(
            Instruction(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(4)),
            Instruction(1, CilOperation.Branch, new CilOperand.BranchTarget(4)),
            Instruction(2, CilOperation.LoadNull),
            Instruction(3, CilOperation.Throw),
            Instruction(4, CilOperation.LoadInt32, new CilOperand.ConstantI4(16)),
            Instruction(5, CilOperation.BranchIfGreaterThanSigned, new CilOperand.BranchTarget(7)),
            Instruction(6, CilOperation.Return),
            Instruction(7, CilOperation.Return));

        var rewritten = new DeadJumpRewriter(Graph(body)).Rewrite(body);

        Assert.Equal(
            [CilOperation.LoadInt32, CilOperation.Nop, CilOperation.Nop, CilOperation.Nop, CilOperation.LoadInt32],
            rewritten.Instructions.Take(5).Select(static instruction => instruction.Operation));
        var folded = ConstantBranchFoldingFactory.Create(new ControlFlowGraphBuilderFactory().Create()).Rewrite(body);
        Assert.Equal(CilOperation.Nop, folded.Instructions[5].Operation);
    }

    // The skipped code is reachable from elsewhere, so the jump stays.
    [Fact]
    public void JumpAcrossLiveCodeStays()
    {
        var body = Body(
            Instruction(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            Instruction(1, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(3)),
            Instruction(2, CilOperation.Branch, new CilOperand.BranchTarget(5)),
            Instruction(3, CilOperation.LoadNull),
            Instruction(4, CilOperation.Throw),
            Instruction(5, CilOperation.Return));

        Assert.Same(body, new DeadJumpRewriter(Graph(body)).Rewrite(body));
    }

    // A jump back into a loop is never a fall-through.
    [Fact]
    public void BackwardJumpStays()
    {
        var body = Body(
            Instruction(0, CilOperation.Nop),
            Instruction(1, CilOperation.Branch, new CilOperand.BranchTarget(0)));

        Assert.Same(body, new DeadJumpRewriter(Graph(body)).Rewrite(body));
    }
}
