// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Text;

namespace NetWasm.Compiler.ComponentModel.Binary;

internal ref struct WasmBinaryReader
{
    private readonly ReadOnlySpan<byte> _bytes;

    public WasmBinaryReader(ReadOnlySpan<byte> bytes)
    {
        _bytes = bytes;
    }

    public int Offset { get; private set; }
    public bool IsAtEnd => Offset == _bytes.Length;

    public void RequireHeader()
    {
        var header = ReadBytes((uint)WasmBinaryFormat.Header.Length);
        if (!header.SequenceEqual(WasmBinaryFormat.Header))
        {
            throw Invalid("core module has an invalid WebAssembly header");
        }
    }

    public byte ReadByte()
    {
        if (Offset >= _bytes.Length)
        {
            throw Invalid("core module is truncated");
        }
        return _bytes[Offset++];
    }

    public uint ReadUnsigned()
    {
        var value = 0u;
        for (var shift = 0; ; shift += 7)
        {
            var next = ReadByte();
            if (shift == 28 && next > 15)
                throw Invalid("core module contains an invalid unsigned LEB128 value");
            value |= (uint)(next & 0x7f) << shift;
            if ((next & 0x80) == 0)
            {
                return value;
            }
        }
    }

    public ReadOnlySpan<byte> ReadBytes(uint count)
    {
        if (count > int.MaxValue || Offset > _bytes.Length - (int)count)
        {
            throw Invalid("core module is truncated");
        }
        var result = _bytes.Slice(Offset, (int)count);
        Offset += (int)count;
        return result;
    }

    public string ReadString()
    {
        var bytes = ReadBytes(ReadUnsigned());
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw Invalid("core module export name is not valid UTF-8");
        }
    }

    public void RequireEnd(string description)
    {
        if (!IsAtEnd)
        {
            throw Invalid($"core module {description} contains trailing data");
        }
    }

    private static Compiler.Core.CompilerException Invalid(string message) =>
        ComponentException.Invalid(message);
}
