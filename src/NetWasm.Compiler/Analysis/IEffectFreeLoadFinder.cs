// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

/// <summary>Finds the load without effects that pushes the value an instruction consumes, so a rule can drop both.</summary>
internal interface IEffectFreeLoadFinder
{
    // Where, in one block's instructions, the load that pushes the value consumed at end starts, or -1.
    int FindStart(IReadOnlyList<CilInstruction> operations, int end);
}
