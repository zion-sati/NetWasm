// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

// The spans a backward branch closes, from its target to the branch: an instruction inside one runs once for each time
// the loop repeats.
internal static class LoopSpans
{
    public static ImmutableArray<(int Start, int End)> Of(CilMethodBody body) =>
    [
        .. body.Instructions
            .Where(static instruction => instruction.Operand is CilOperand.BranchTarget target &&
                target.Offset <= instruction.Offset)
            .Select(static instruction => (((CilOperand.BranchTarget)instruction.Operand).Offset, instruction.Offset)),
    ];

    public static bool Contain(IEnumerable<(int Start, int End)> loops, int offset) =>
        loops.Any(loop => loop.Start <= offset && offset <= loop.End);
}
