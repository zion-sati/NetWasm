// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Buffers.Binary;
using System.Text;

namespace NetWasm.Compiler.ComponentModel.Debugging;

// Reads a method-symbols sidecar (<program>.wasm.method-symbols), which the compiler writes: u32 magic "NWSY", u16
// version 1, u16 kind 2, a build ID of 32 UTF-16 characters, u32 entry count, u32 strings per entry (1), then per entry
// a u32 ID, a u32 character offset and an i32 character length (-1 for none), then the UTF-16 characters. A method's
// ID names it ("Program::Main"); a source location's ID names its method and line ("Program::Main in Program.cs:line 8").
internal static class MethodSymbolsFormat
{
    private const uint Magic = 0x5953574E;
    private const ushort Version = 1;
    private const ushort MethodKind = 2;
    private const int HeaderSize = 4 + 2 + 2 + 32 * sizeof(char) + 4 + 4;
    private const int EntrySize = 4 + 4 + 4;

    // The most IDs a sidecar may name, well past any program's symbols, so a corrupt ID cannot size a huge table.
    private const int MaximumId = 1 << 26;

    // Each entry's text by its ID (the compiler numbers them from 1); null where there is none.
    public static string?[] Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < HeaderSize ||
            BinaryPrimitives.ReadUInt32LittleEndian(bytes) != Magic ||
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[4..]) != Version ||
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[6..]) != MethodKind)
        {
            throw Invalid("is not a version 1 method-symbols sidecar");
        }
        var count = BinaryPrimitives.ReadInt32LittleEndian(bytes[(HeaderSize - 8)..]);
        var stringsPerEntry = BinaryPrimitives.ReadInt32LittleEndian(bytes[(HeaderSize - 4)..]);
        if (stringsPerEntry != 1 || count < 0 || count > (bytes.Length - HeaderSize) / EntrySize)
            throw Invalid("declares entries it does not hold");
        var characters = bytes[(HeaderSize + count * EntrySize)..];
        if (characters.Length % sizeof(char) != 0) throw Invalid("holds a partial character");
        var text = Encoding.Unicode.GetString(characters);
        var maximum = 0;
        for (var index = 0; index < count; index++)
        {
            var id = BinaryPrimitives.ReadInt32LittleEndian(bytes[(HeaderSize + index * EntrySize)..]);
            if (id <= 0 || id > MaximumId) throw Invalid($"names entry {id}, outside 1 to {MaximumId}");
            maximum = Math.Max(maximum, id);
        }
        var result = new string?[maximum + 1];
        var seen = new bool[maximum + 1];
        for (var index = 0; index < count; index++)
        {
            var entry = bytes[(HeaderSize + index * EntrySize)..];
            var id = BinaryPrimitives.ReadInt32LittleEndian(entry);
            var offset = BinaryPrimitives.ReadInt32LittleEndian(entry[4..]);
            var length = BinaryPrimitives.ReadInt32LittleEndian(entry[8..]);
            if (seen[id]) throw Invalid($"holds entry {id} twice");
            seen[id] = true;
            if (length < 0) continue;
            if (offset < 0 || offset > text.Length - length) throw Invalid($"names entry {id} past its characters");
            result[id] = text.Substring(offset, length);
        }
        return result;
    }

    private static Core.CompilerException Invalid(string problem) =>
        ComponentException.Invalid($"the method symbols for debug information {problem}");
}
