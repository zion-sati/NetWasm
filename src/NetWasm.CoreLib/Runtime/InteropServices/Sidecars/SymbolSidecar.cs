namespace System.Runtime.InteropServices.Sidecars
{
    using System.Buffers.Binary;

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

        internal int Id(int entry) =>
            BinaryPrimitives.ReadInt32LittleEndian(_bytes.AsSpan(HeaderSize + entry * _entrySize));

        // The string's UTF-16 text, or none (a type without a namespace).
        internal ReadOnlySpan<char> Text(int entry, int index, out bool present)
        {
            var field = HeaderSize + entry * _entrySize + sizeof(int) + index * sizeof(int) * 2;
            var offset = BinaryPrimitives.ReadInt32LittleEndian(_bytes.AsSpan(field));
            var length = BinaryPrimitives.ReadInt32LittleEndian(_bytes.AsSpan(field + sizeof(int)));
            present = length >= 0;
            return present
                ? MemoryMarshal.Cast<byte, char>(
                    _bytes.AsSpan(_characters + offset * sizeof(char), length * sizeof(char)))
                : default;
        }

        internal string? String(int entry, int index)
        {
            var text = Text(entry, index, out var present);
            return present ? new string(text) : null;
        }

        internal int StringsPerEntry => _stringsPerEntry;

        internal int CharacterCount => (_bytes.Length - _characters) / sizeof(char);
    }
}
