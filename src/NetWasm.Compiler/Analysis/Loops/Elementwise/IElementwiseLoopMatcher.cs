// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Loops.Elementwise;

internal interface IElementwiseLoopMatcher
{
    // The counted loop whose entry jump is at `jump` and whose test starts at `condition`, if the vector loop can
    // take it.
    ElementwiseLoop? Match(ImmutableArray<CilInstruction> instructions, int jump, int condition, ImmutableArray<CliValueKind> locals);
}
