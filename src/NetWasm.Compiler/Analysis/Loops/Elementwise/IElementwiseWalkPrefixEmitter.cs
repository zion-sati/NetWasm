// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Loops.Elementwise;

internal interface IElementwiseWalkPrefixEmitter
{
    // The instruction that falls into a walk's head, then the vector loop ahead of the head; `exit` follows the
    // walk, and `depth` is the stack the vector loop needs.
    List<CilInstruction> Emit(
        CilInstruction entry,
        ElementwiseWalk walk,
        int exit,
        ImmutableArray<CliValueKind>.Builder locals,
        ImmutableArray<CliTypeIdentity>.Builder localTypes,
        out int depth);
}
