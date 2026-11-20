// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace NetWasm.Runtime.Pack.Materialization;

// The symbols an archive defines, from the GNU symbol table that llvm-ar writes first for WebAssembly objects on
// every host: a member named "/" (or "/SYM64/" with 64-bit fields) holding a big-endian count, that many member
// offsets, then the names, each ending in a zero byte.
internal static class RuntimeArchiveSymbolTable
{
    private const int HeaderSize = 60;

    public static ImmutableHashSet<string> Parse(ReadOnlySpan<byte> archive)
    {
        if (!archive.StartsWith("!<arch>\n"u8) || archive.Length < 8 + HeaderSize)
            throw new InvalidOperationException("The archive is not a regular ar archive.");
        var header = archive.Slice(8, HeaderSize);
        var name = Encoding.ASCII.GetString(header[..16]).TrimEnd();
        var wide = name == "/SYM64/";
        if (name != "/" && !wide)
            throw new InvalidOperationException("The archive has no symbol table.");
        if (!int.TryParse(Encoding.ASCII.GetString(header.Slice(48, 10)).Trim(), NumberStyles.None,
                CultureInfo.InvariantCulture, out var size) || size > archive.Length - 8 - HeaderSize)
            throw new InvalidOperationException("The archive's symbol table is truncated.");
        var table = archive.Slice(8 + HeaderSize, size);
        var width = wide ? sizeof(ulong) : sizeof(uint);
        var count = table.Length < width ? ulong.MaxValue
            : wide ? BinaryPrimitives.ReadUInt64BigEndian(table) : BinaryPrimitives.ReadUInt32BigEndian(table);
        if (count > (ulong)((table.Length - width) / width))
            throw new InvalidOperationException("The archive's symbol table is truncated.");
        var names = table[(width + (int)count * width)..];
        var symbols = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        for (var index = 0UL; index < count; index++)
        {
            var end = names.IndexOf((byte)0);
            if (end < 0)
                throw new InvalidOperationException("The archive's symbol table is truncated.");
            symbols.Add(Encoding.ASCII.GetString(names[..end]));
            names = names[(end + 1)..];
        }
        return symbols.ToImmutable();
    }
}
