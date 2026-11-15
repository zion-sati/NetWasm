// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Constants;

internal interface IConstantOperandReader
{
    // The longest producer sequence the reader recognizes.
    int MaximumLength { get; }

    ScalarConstant? Read(IReadOnlyList<CilInstruction> producers);
}
