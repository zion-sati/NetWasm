// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Loops.Elementwise;

internal interface IElementwiseBodyParser
{
    // Reads a loop body as element stores, folds, copies and index checks over the counter, or the walking
    // reference; null when it does anything else.
    ElementwiseBody? Parse(CilInstruction[] body, int counter, HashSet<int> written, int reference = -1);
}
