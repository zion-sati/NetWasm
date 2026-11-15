// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Wasm.Encoding;

/// <summary>
/// Reads an immutable snapshot of bytes accumulated by a binary buffer.
/// </summary>
public interface IWasmBinarySnapshotReader
{
    byte[] Read();
}
