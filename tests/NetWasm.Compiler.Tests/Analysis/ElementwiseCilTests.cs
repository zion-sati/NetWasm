// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Analysis.Loops.Elementwise;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Analysis;

// Two loads name the same array when they load the same local, or the same argument: a local and an argument of one
// index are two arrays.
public sealed class ElementwiseCilTests
{
    [Theory]
    [InlineData(CilOperation.LoadLocal, 0, CilOperation.LoadLocal, 0, true)]
    [InlineData(CilOperation.LoadLocal, 0, CilOperation.LoadLocal, 1, false)]
    [InlineData(CilOperation.LoadLocal, 0, CilOperation.LoadArgument, 0, false)]
    public void TwoLoadsOfOneSlotNameOneArray(CilOperation left, int leftIndex, CilOperation right, int rightIndex, bool same) =>
        Assert.Equal(same, ElementwiseCil.SameArray(
            new CilInstruction(0, 1, left, new CilOperand.Index(leftIndex)),
            new CilInstruction(1, 2, right, new CilOperand.Index(rightIndex))));
}
