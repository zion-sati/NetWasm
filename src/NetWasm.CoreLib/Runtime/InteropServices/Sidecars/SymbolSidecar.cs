// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices.Sidecars
{
    using System.Buffers.Binary;
    using System.Runtime.CompilerServices;

    // A parsed symbols sidecar (the compiler's SymbolSidecarWriter format): its entries' IDs and strings, read
    // from the file's bytes as they are needed.
    internal sealed class SymbolSidecar
    {
        internal const uint Magic = 0x5953574E;
        internal const ushort Version = 1;
        internal const ushort TypeKind = 1;
        internal const ushort MethodKind = 2;
        internal const int BuildIdLength = 32;
        internal const int HeaderSize = 4 + 2 + 2 + BuildIdLength * sizeof(char) + 4 + 4;

        private readonly byte[] _bytes;
        private readonly int _stringsPerEntry;
        private readonly int _entrySize;
        private readonly int _characters;

        internal SymbolSidecar(byte[] bytes, int count, int stringsPerEntry)
        {
            _bytes = bytes ?? throw new ArgumentNullException();
            Count = count;
            _stringsPerEntry = stringsPerEntry;
            _entrySize = sizeof(int) + stringsPerEntry * sizeof(int) * 2;
            _characters = HeaderSize + count * _entrySize;
        }

        internal int Count { get; }

        internal int Id(int entry) => ReadInt32(HeaderSize + entry * _entrySize);

        // The string's UTF-16 text, or none (a type without a namespace).
        internal ReadOnlySpan<char> Text(int entry, int index, out bool present)
        {
            var field = HeaderSize + entry * _entrySize + sizeof(int) + index * sizeof(int) * 2;
            var offset = ReadInt32(field);
            var length = ReadInt32(field + sizeof(int));
            present = length >= 0;
            return present
                ? MemoryMarshal.Cast<byte, char>(
                    _bytes.AsSpan(_characters + offset * sizeof(char), length * sizeof(char)))
                : default;
        }

        // Whether the string's field is none or a range inside the file's text.
        internal bool StringFits(int entry, int index)
        {
            var field = HeaderSize + entry * _entrySize + sizeof(int) + index * sizeof(int) * 2;
            var offset = ReadInt32(field);
            var length = ReadInt32(field + sizeof(int));
            return length == -1 || (length >= 0 && offset >= 0 && (long)offset + length <= CharacterCount);
        }

        internal string? String(int entry, int index)
        {
            var text = Text(entry, index, out var present);
            return present ? new string(text) : null;
        }

        internal int StringsPerEntry => _stringsPerEntry;

        // A little-endian int32 of the file, whose entries SymbolSidecarParser has checked lie inside it before anything
        // reads them: one read with no bounds check of its own, as WebAssembly memory is little-endian.
        private int ReadInt32(int offset) =>
            Unsafe.ReadUnaligned<int>(ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_bytes), offset));

        internal int CharacterCount => (_bytes.Length - _characters) / sizeof(char);
    }
}
