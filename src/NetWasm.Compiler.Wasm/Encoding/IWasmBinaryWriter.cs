// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;

namespace NetWasm.Compiler.Wasm.Encoding;

/// <summary>
/// Raw byte sink used by encoding Strategies.
/// </summary>
public interface IWasmBinaryWriter
{
    void Write(ReadOnlySpan<byte> bytes);
}
