// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Analysis.Constants;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

// Code that no path from a method's entry or one of its handlers reaches never runs, and emission never places it (the
// structurer lays out reachable blocks only), so nothing only such code names is reached, rooted or compiled. The body
// the closure records has that code erased, so every scan of it agrees: the closure's (calls, allocations, type tests,
// field, token and string references, delegate creation, dispatch sites, implicit exceptions), the receiver flow's and
// the module planners'. Folding erases what the branches it decides cut off; this catches what the managed optimizer
// leaves after it, such as the code after an inlined callee that never returns.
internal static class UnreachableCodeEraser
{
    // The proofs the body carries speak of its reachable instructions, which erasure keeps as they are (the same
    // instructions, at the same offsets), so a proof that described the body describes the erased one.
    public static CilMethodBody Erase(CilMethodBody body, ControlFlowGraph graph)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(graph);
        var erased = UnreachableInstructionRewriter.Erase(body, graph);
        if (ReferenceEquals(erased, body)) return body;
        return erased with
        {
            ArrayBoundsProof = body.ArrayBoundsProof is { } bounds && bounds.Instructions == body.Instructions
                ? bounds with { Instructions = erased.Instructions }
                : body.ArrayBoundsProof,
            IntegerCheckProof = body.IntegerCheckProof is { } checks && checks.Instructions == body.Instructions
                ? checks with { Instructions = erased.Instructions }
                : body.IntegerCheckProof,
        };
    }
}
