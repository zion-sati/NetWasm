// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;

namespace NetWasm.Compiler.Wasm.Units;

internal interface IWasmIndexOperandScanner
{
    // Lists, in body order, every module index a function body (its local declarations
    // and code) encodes. Throws InvalidDataException for an instruction it cannot decode.
    ImmutableArray<WasmIndexOperand> Scan(ReadOnlySpan<byte> body);
}
