using System.Text;

namespace NetWasm.TestInfrastructure;

public static class WasmModuleInspection
{
    private static ReadOnlySpan<byte> Header =>
        [0x00, 0x61, 0x73, 0x6d, 0x01, 0x00, 0x00, 0x00];

    public static IReadOnlyList<string> ReadExportNames(ReadOnlySpan<byte> module)
    {
        var reader = new Reader(module);
        Assert.True(reader.ReadBytes(Header.Length).SequenceEqual(Header));
        while (!reader.IsAtEnd)
        {
            var sectionId = reader.ReadByte();
            var section = new Reader(reader.ReadBytes(reader.ReadUnsigned()));
            if (sectionId != 7)
            {
                continue;
            }

            var names = new List<string>();
            var count = section.ReadUnsigned();
            for (var index = 0u; index < count; index++)
            {
                names.Add(section.ReadString());
                section.ReadByte();
                section.ReadUnsigned();
            }
            Assert.True(section.IsAtEnd);
            return names;
        }

        return [];
    }

    private ref struct Reader
    {
        private readonly ReadOnlySpan<byte> _bytes;
        private int _offset;

        public Reader(ReadOnlySpan<byte> bytes)
        {
            _bytes = bytes;
            _offset = 0;
        }

        public bool IsAtEnd => _offset == _bytes.Length;

        public byte ReadByte()
        {
            Assert.True(_offset < _bytes.Length);
            return _bytes[_offset++];
        }

        public uint ReadUnsigned()
        {
            var value = 0u;
            for (var shift = 0; shift < 35; shift += 7)
            {
                var next = ReadByte();
                value |= (uint)(next & 0x7f) << shift;
                if ((next & 0x80) == 0)
                {
                    return value;
                }
            }

            Assert.Fail("Wasm section contains an invalid unsigned integer.");
            return 0;
        }

        public ReadOnlySpan<byte> ReadBytes(int count)
        {
            Assert.InRange(count, 0, _bytes.Length - _offset);
            var result = _bytes.Slice(_offset, count);
            _offset += count;
            return result;
        }

        public ReadOnlySpan<byte> ReadBytes(uint count)
        {
            Assert.True(count <= int.MaxValue);
            return ReadBytes((int)count);
        }

        public string ReadString() =>
            Encoding.UTF8.GetString(ReadBytes(ReadUnsigned()));
    }
}
