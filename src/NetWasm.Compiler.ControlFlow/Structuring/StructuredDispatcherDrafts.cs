// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Linq;
using NetWasm.Compiler.ControlFlow.Draft;

namespace NetWasm.Compiler.ControlFlow.Structuring;

// Whether a structured sequence routes any of its transfers through a dispatcher's program counter.
internal static class StructuredDispatcherDrafts
{
    public static bool Contains(StructuredSequenceDraft sequence) =>
        sequence.Regions.Any(region => region switch
        {
            StructuredDispatcherDraft => true,
            StructuredIfDraft conditional => Contains(conditional.WhenTrue) || Contains(conditional.WhenFalse),
            StructuredLoopDraft loop => Contains(loop.Body) || Contains(loop.ContinueBody) || Contains(loop.ExitBody),
            StructuredPostTestLoopDraft loop => Contains(loop.Body) || Contains(loop.ContinueBody) || Contains(loop.ExitBody),
            _ => false,
        });
}
