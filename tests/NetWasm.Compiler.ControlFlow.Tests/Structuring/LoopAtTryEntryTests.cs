// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Tests.Structuring;

using static ControlFlowTestSupport;

// A loop whose header is a try's first block lies inside the try. Structurize validates that every reachable block
// is structured exactly once.
public sealed class LoopAtTryEntryTests
{
    // try { try { loop: if (0) leave mid; if (0) br loop; nop; leave mid } finally { } mid: nop; leave end }
    // finally { } end: ret. Both leaves from the loop go to mid, which is also the loop's exit, as the leave chainer
    // leaves a using block's return and break. The inner try's continuation is mid's code, not a break out of the
    // loop.
    [Fact]
    public void ALoopInsideATryLeavesItsBreakToTheTrysContinuation()
    {
        var body = Body(CliValueKind.Void, 2, [],
            I(0, CilOperation.Nop),
            I(1, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(2, CilOperation.BranchIfFalse, new CilOperand.BranchTarget(4)),
            I(3, CilOperation.Leave, new CilOperand.BranchTarget(9)),
            I(4, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(5, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(0)),
            I(6, CilOperation.Nop),
            I(7, CilOperation.Leave, new CilOperand.BranchTarget(9)),
            I(8, CilOperation.EndFinally),
            I(9, CilOperation.Nop),
            I(10, CilOperation.Leave, new CilOperand.BranchTarget(12)),
            I(11, CilOperation.EndFinally),
            I(12, CilOperation.Return)) with
        {
            ExceptionRegions =
            [
                new(CilExceptionRegionKind.Finally, 0, 8, 8, 1, null, null),
                new(CilExceptionRegionKind.Finally, 0, 11, 11, 1, null, null),
            ],
        };

        Assert.NotNull(Structurize(body));
    }
}
