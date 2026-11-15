// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm;

/// <summary>
/// Raw byte sink used by binary encoding actors.
/// </summary>
public sealed class WasmBinaryWriter : IWasmBinaryWriter
{
    private readonly WasmBinaryBuffer _buffer;

    public WasmBinaryWriter(WasmBinaryBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        _buffer = buffer;
    }

    public void Write(ReadOnlySpan<byte> bytes) => _buffer.Append(bytes);
}
