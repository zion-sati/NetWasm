// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm;

/// <summary>
/// Snapshot reader for one mutable binary buffer.
/// </summary>
public sealed class WasmBinarySnapshotReader : IWasmBinarySnapshotReader
{
    private readonly WasmBinaryBuffer _buffer;

    public WasmBinarySnapshotReader(WasmBinaryBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        _buffer = buffer;
    }

    public byte[] Read() => _buffer.Snapshot();
}
