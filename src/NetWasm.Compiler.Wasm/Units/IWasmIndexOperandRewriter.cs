// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;

namespace NetWasm.Compiler.Wasm.Units;

internal interface IWasmIndexOperandRewriter
{
    // Copies a function body with each scanned index replaced by its remapped value,
    // encoded as the compiler encodes it: minimal LEB128.
    byte[] Rewrite(
        ReadOnlySpan<byte> body,
        ImmutableArray<WasmIndexOperand> operands,
        IWasmIndexRemapping remapping);
}
