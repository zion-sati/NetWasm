using NetWasm.Compiler.ControlFlow;

namespace NetWasm.Compiler.Analysis.Constants;

internal static class ConstantBranchFoldingFactory
{
    public static ConstantBranchFolding Create(IControlFlowGraphBuilder graphs) =>
        new([new ConstantBranchRewriter(graphs), new UnreachableInstructionRewriter(graphs), new DeadJumpRewriter(graphs)]);
}
