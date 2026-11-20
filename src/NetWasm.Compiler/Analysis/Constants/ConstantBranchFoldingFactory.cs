// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ControlFlow;

namespace NetWasm.Compiler.Analysis.Constants;

internal static class ConstantBranchFoldingFactory
{
    public static ConstantBranchFolding Create(IControlFlowGraphBuilder graphs) =>
        new([
            new ConstantBranchRewriter(graphs, new ConstantValueFlow(new StackEffectResolver(OperandCalledMethodResolver.Instance))),
            new UnreachableInstructionRewriter(graphs),
            new DeadJumpRewriter(graphs),
        ]);
}
