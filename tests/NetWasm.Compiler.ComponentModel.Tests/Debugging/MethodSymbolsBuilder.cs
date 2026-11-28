// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Buffers.Binary;
using System.Text;

namespace NetWasm.Compiler.ComponentModel.Tests.Debugging;

// Writes a method-symbols sidecar as the compiler does, with knobs to corrupt it.
internal static class MethodSymbolsBuilder
{
    public static byte[] Build(IReadOnlyList<(int Id, string? Text)> entries, ushort version = 1, ushort kind = 2,
        int? count = null, int stringsPerEntry = 1)
    {
        var characters = new StringBuilder();
        var header = new byte[80 + entries.Count * 12];
        BinaryPrimitives.WriteUInt32LittleEndian(header, 0x5953574E);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), version);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6), kind);
        Encoding.Unicode.GetBytes(new string('0', 32), header.AsSpan(8));
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(72), count ?? entries.Count);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(76), stringsPerEntry);
        for (var index = 0; index < entries.Count; index++)
        {
            var at = 80 + index * 12;
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(at), entries[index].Id);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(at + 4), characters.Length);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(at + 8), entries[index].Text?.Length ?? -1);
            characters.Append(entries[index].Text);
        }
        return [.. header, .. Encoding.Unicode.GetBytes(characters.ToString())];
    }
}
