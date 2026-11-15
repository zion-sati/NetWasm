// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Wasm.Units;

/// <summary>
/// One module index encoded in a function body: where its LEB128 starts and how many
/// bytes it takes, the index space it names and its value. Type indices in block and
/// heap types are signed 33-bit LEB128; every other index is unsigned.
/// </summary>
internal readonly record struct WasmIndexOperand(
    int Offset,
    int Length,
    WasmIndexSpace Space,
    uint Index,
    bool IsSigned33 = false);
