namespace System.Runtime.InteropServices.Sidecars
{
    using System.Buffers.Binary;

    // Checks a whole sidecar before anything reads it: its header, kind and build ID, and that every entry and
    // string lies inside the file.
    internal sealed class SymbolSidecarParser : ISymbolSidecarParser
    {
        public SymbolSidecar Parse(byte[] bytes, ushort kind, string buildId)
        {
            if (bytes == null || buildId == null)
            {
                throw new ArgumentNullException();
            }
            var span = bytes.AsSpan();
            if (span.Length < SymbolSidecar.HeaderSize ||
                BinaryPrimitives.ReadUInt32LittleEndian(span) != SymbolSidecar.Magic)
            {
                throw new FormatException("it is not a NetWasm symbols file");
            }
            if (BinaryPrimitives.ReadUInt16LittleEndian(span[4..]) != SymbolSidecar.Version ||
                BinaryPrimitives.ReadUInt16LittleEndian(span[6..]) != kind)
            {
                throw new FormatException("it is another version or kind of symbols file");
            }
            var fileBuildId = MemoryMarshal.Cast<byte, char>(span.Slice(8, SymbolSidecar.BuildIdLength * sizeof(char)));
            if (!fileBuildId.SequenceEqual(buildId))
            {
                throw new FormatException("it is from another build of the program");
            }
            var count = BinaryPrimitives.ReadInt32LittleEndian(span[(8 + SymbolSidecar.BuildIdLength * sizeof(char))..]);
            var stringsPerEntry = BinaryPrimitives.ReadInt32LittleEndian(
                span[(12 + SymbolSidecar.BuildIdLength * sizeof(char))..]);
            var expected = kind == SymbolSidecar.TypeKind ? 4 : 1;
            var entrySize = sizeof(int) + expected * sizeof(int) * 2L;
            if (stringsPerEntry != expected || count < 0 ||
                SymbolSidecar.HeaderSize + count * entrySize > span.Length ||
                (span.Length - (SymbolSidecar.HeaderSize + count * entrySize)) % sizeof(char) != 0)
            {
                throw new FormatException("its entries do not fit the file");
            }
            var sidecar = new SymbolSidecar(bytes, count, stringsPerEntry);
            for (var entry = 0; entry < count; entry++)
            {
                if (sidecar.Id(entry) < 0 || (entry > 0 && sidecar.Id(entry) <= sidecar.Id(entry - 1)))
                {
                    throw new FormatException("its IDs are not in order");
                }
                var field = SymbolSidecar.HeaderSize + entry * (int)entrySize + sizeof(int);
                for (var index = 0; index < stringsPerEntry; index++, field += sizeof(int) * 2)
                {
                    var offset = BinaryPrimitives.ReadInt32LittleEndian(span[field..]);
                    var length = BinaryPrimitives.ReadInt32LittleEndian(span[(field + sizeof(int))..]);
                    if (length < -1 || (length >= 0 && (offset < 0 || (long)offset + length > sidecar.CharacterCount)))
                    {
                        throw new FormatException("a string lies outside the file");
                    }
                }
            }
            return sidecar;
        }
    }
}
