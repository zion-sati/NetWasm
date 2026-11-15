// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness;

internal static class GeneratedCilReductionTestPrograms
{
    public static GeneratedCilReductionCase StraightLine() => new(
        new(
            17,
            CliValueKind.I4,
            [CliValueKind.I4],
            [CliValueKind.I4],
            [
                new(
                    0,
                    [],
                    [],
                    [
                        new(CilOperation.LoadInt32,
                            new GeneratedCilOperand.Int32(173)),
                        new(CilOperation.Pop, new GeneratedCilOperand.None()),
                        new(CilOperation.LoadInt32,
                            new GeneratedCilOperand.Int32(91)),
                        new(CilOperation.Pop, new GeneratedCilOperand.None()),
                        new(CilOperation.LoadArgument,
                            new GeneratedCilOperand.Index(0)),
                        new(CilOperation.Return, new GeneratedCilOperand.None()),
                    ]),
            ]),
        [-17, 0, 19]);
}
