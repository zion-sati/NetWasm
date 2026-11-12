using System.Buffers.Binary;
using System.Text;

namespace NetWasm.Compiler.Tests.Symbols;

// Reads the sidecar format independently of the writer: header fields, then every entry's ID and strings.
internal sealed record SymbolSidecarContents(
    uint Magic,
    ushort Version,
    ushort Kind,
    string BuildId,
    IReadOnlyList<(int Id, string?[] Strings)> Entries)
{
    public static SymbolSidecarContents Read(byte[] bytes)
    {
        var span = bytes.AsSpan();
        var buildId = Encoding.Unicode.GetString(span.Slice(8, 64));
        var count = BinaryPrimitives.ReadInt32LittleEndian(span[72..]);
        var stringsPerEntry = BinaryPrimitives.ReadInt32LittleEndian(span[76..]);
        var entrySize = 4 + stringsPerEntry * 8;
        var characters = 80 + count * entrySize;
        var entries = new List<(int, string?[])>();
        for (var entry = 0; entry < count; entry++)
        {
            var field = 80 + entry * entrySize;
            var strings = new string?[stringsPerEntry];
            for (var index = 0; index < stringsPerEntry; index++)
            {
                var offset = BinaryPrimitives.ReadInt32LittleEndian(span[(field + 4 + index * 8)..]);
                var length = BinaryPrimitives.ReadInt32LittleEndian(span[(field + 8 + index * 8)..]);
                strings[index] = length < 0
                    ? null
                    : Encoding.Unicode.GetString(span.Slice(characters + offset * 2, length * 2));
            }
            entries.Add((BinaryPrimitives.ReadInt32LittleEndian(span[field..]), strings));
        }
        return new(
            BinaryPrimitives.ReadUInt32LittleEndian(span),
            BinaryPrimitives.ReadUInt16LittleEndian(span[4..]),
            BinaryPrimitives.ReadUInt16LittleEndian(span[6..]),
            buildId,
            entries);
    }

    // The display text of each type entry, or each method entry's frame text.
    public IEnumerable<string?> Texts => Entries.Select(entry => entry.Strings[^1]);
}
