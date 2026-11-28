// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

// A local is live where an instruction starts when some path from there reads it before writing it.
public sealed class CilLocalLivenessTests
{
    // 0 ldc 1; 1 brfalse 5; 2 ldloc.0; 3 pop; 4 br 7; 5 ldc 2; 6 stloc.0; 7 ldloc.0; 8 pop; 9 ret
    private static readonly ImmutableArray<CilInstruction> Join = Body(
        (CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
        (CilOperation.BranchIfFalse, new CilOperand.BranchTarget(5)),
        (CilOperation.LoadLocal, new CilOperand.Index(0)),
        (CilOperation.Pop, new CilOperand.None()),
        (CilOperation.Branch, new CilOperand.BranchTarget(7)),
        (CilOperation.LoadInt32, new CilOperand.ConstantI4(2)),
        (CilOperation.StoreLocal, new CilOperand.Index(0)),
        (CilOperation.LoadLocal, new CilOperand.Index(0)),
        (CilOperation.Pop, new CilOperand.None()),
        (CilOperation.Return, new CilOperand.None()));

    // 0 ldc 0; 1 stloc.1; 2 br 6; 3 ldloc.0; 4 pop; 5 nop; 6 ldloc.1; 7 brtrue 3; 8 ret
    private static readonly ImmutableArray<CilInstruction> Loop = Body(
        (CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
        (CilOperation.StoreLocal, new CilOperand.Index(1)),
        (CilOperation.Branch, new CilOperand.BranchTarget(6)),
        (CilOperation.LoadLocal, new CilOperand.Index(0)),
        (CilOperation.Pop, new CilOperand.None()),
        (CilOperation.Nop, new CilOperand.None()),
        (CilOperation.LoadLocal, new CilOperand.Index(1)),
        (CilOperation.BranchIfTrue, new CilOperand.BranchTarget(3)),
        (CilOperation.Return, new CilOperand.None()));

    [Theory]
    // Both arms of the join reach the read at 7, but the false arm writes the local first.
    [InlineData(0, true)]
    [InlineData(2, true)]
    [InlineData(4, true)]
    [InlineData(5, false)]
    [InlineData(7, true)]
    [InlineData(8, false)]
    public void ALocalIsLiveWhereAPathReadsItBeforeAWrite(int index, bool live) =>
        Assert.Equal(live, Liveness(Join, 0).IsLive(0, index));

    [Theory]
    // The loop reads local 0 on every iteration, so it is live throughout it and before it; local 1 is written before
    // the loop and read by its condition.
    [InlineData(0, 0, true)]
    [InlineData(0, 5, true)]
    [InlineData(0, 8, false)]
    [InlineData(1, 0, false)]
    [InlineData(1, 2, true)]
    [InlineData(1, 5, true)]
    [InlineData(1, 8, false)]
    public void ALocalTheLoopReadsIsLiveAroundItsBackEdge(int local, int index, bool live) =>
        Assert.Equal(live, Liveness(Loop, 0, 1).IsLive(local, index));

    private static CilLocalLiveness Liveness(ImmutableArray<CilInstruction> body, params int[] locals) =>
        CilLocalLiveness.Compute(body, Enumerable.Range(0, body.Length).ToDictionary(static index => index), locals);

    private static ImmutableArray<CilInstruction> Body(params (CilOperation Operation, CilOperand Operand)[] code) =>
        [.. code.Select(static (instruction, offset) => new CilInstruction(offset, offset + 1, instruction.Operation, instruction.Operand))];
}
