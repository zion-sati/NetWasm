using System.Buffers.Binary;
using System.Numerics;

namespace NetWasm.Compiler.Tests.Layout;

// Reads the runtime type table as the runtime decodes it at startup and as CoreLib reads it in
// place, so the encoder's tests check the format through its readers' arithmetic.
internal sealed class RuntimeTypeTableReader(byte[] table)
{
    private const long HeaderBits = 20 * 32;

    internal uint Header(int field) => Read(field * 32L, 32);

    internal int Capacity => (int)Header(1);

    private int Width(int field, int shift) => (int)(Header(field) >> shift & 63);

    internal int IdBits => Width(2, 0);
    private int ShapeIndexBits => Width(2, 6);
    private int FlagIndexBits => Width(2, 12);
    private int LayoutIndexBits => Width(2, 18);
    private int ShapeBits => Width(2, 24);
    internal int DefinitionBits => Width(3, 24);
    internal int AttributeBits => ShapeBits - FlagIndexBits - LayoutIndexBits - DefinitionBits;
    private int AssignableCountBits => Width(3, 0);
    private int GenericCountBits => Width(3, 6);
    private int RankBits => Width(3, 12);
    private int SampleBits => Width(3, 18);
    private long Blocks => (Capacity + 255) / 256;

    internal int Shapes => (int)((Header(10) - Header(8)) / Math.Max(1, ShapeBits));

    internal int AssemblyBits => Header(7) == 0 ? 0 : (int)Read(Header(7), 6);

    internal int EntryAssembly => (int)Read(Header(7) + 6, AssemblyBits);

    internal int AssemblyRuns => Header(7) == 0 ? 0 : (int)Read(Header(7) + 6 + AssemblyBits, IdBits + 1);

    // The bit offset of the type's shape: its flag index, layout index and generic definition.
    private long Shape(int id) => Header(8) + Read(Header(6) + (long)id * ShapeIndexBits, ShapeIndexBits) * (long)ShapeBits;

    internal uint FlagWord(int id) => Read(HeaderBits + 32L * Read(Shape(id), FlagIndexBits), 32);

    internal int GenericDefinition(int id) => (int)Read(Shape(id) + FlagIndexBits + LayoutIndexBits, DefinitionBits);

    // The TypeAttributes value the shape indexes among the words past the flag words, with the
    // interface, abstract and sealed bits from the flag word.
    internal int Attributes(int id) => (int)Read(HeaderBits + 32 * (Header(4) +
        (long)Read(Shape(id) + FlagIndexBits + LayoutIndexBits + DefinitionBits, AttributeBits)), 32) |
        ((FlagWord(id) & (1 << 2)) != 0 ? 0x20 : 0) | ((FlagWord(id) & (1 << 13)) != 0 ? 0x80 : 0) |
        ((FlagWord(id) & (1 << 11)) != 0 ? 0x100 : 0);

    // The assembly of the last run starting at or before the ID, by the binary search CoreLib runs.
    internal int Assembly(int id)
    {
        if (Header(7) == 0)
        {
            return 0;
        }
        int runBits = IdBits + AssemblyBits, low = 0, high = AssemblyRuns - 1;
        var runs = Header(7) + 6 + AssemblyBits + IdBits + 1;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (Read(runs + (long)middle * runBits, IdBits) <= id) low = middle;
            else high = middle - 1;
        }
        return (int)Read(runs + (long)low * runBits + IdBits, AssemblyBits);
    }

    internal (int Size, bool[] Bitmap, int ValueSize, int Payload, bool[] ValueBitmap) Layout(int id)
    {
        var index = Read(Shape(id) + FlagIndexBits, LayoutIndexBits);
        int size = Width(11, 0), bitmap = Width(11, 6), payload = Width(11, 12);
        var offset = Header(10) + index * (2L * size + 2 * bitmap + payload);
        var objectSize = (int)Read(offset, size);
        var objectBitmap = (int)Read(offset + size, bitmap);
        var valueSize = (int)Read(offset + size + bitmap, size);
        var boxedPayload = (int)Read(offset + 2L * size + bitmap, payload);
        var valueBitmap = (int)Read(offset + 2L * size + bitmap + payload, bitmap);
        return (objectSize, Bitmap(objectBitmap), valueSize, boxedPayload, Bitmap(valueBitmap));
    }

    internal bool[] Bitmap(int index)
    {
        if (index == 0)
        {
            return [];
        }
        int offsetBits = Width(14, 0), lengthBits = Width(14, 6);
        var start = Header(13) + (long)Header(12) * offsetBits + Read(Header(13) + (long)(index - 1) * offsetBits, offsetBits);
        var length = (int)Read(start, lengthBits);
        return [.. Enumerable.Range(0, length).Select(bit => Read(start + lengthBits + bit, 1) != 0)];
    }

    // The base types and extra assignable lists, decoded in ID order as the runtime does once.
    internal (int[] Bases, int[][] Assignable) DecodeRegistry()
    {
        var bases = new int[Capacity];
        var assignable = new int[Capacity][];
        long explicitIds = Header(15) + Capacity, lists = Header(16) + Capacity;
        for (var id = 0; id < Capacity; id++)
        {
            var flagIndex = Read(Shape(id), FlagIndexBits);
            bases[id] = (int)Read(Header(5) + flagIndex * (long)IdBits, IdBits);
            if (Read(Header(15) + id, 1) != 0)
            {
                bases[id] = (int)Read(explicitIds, IdBits);
                explicitIds += IdBits;
            }
            assignable[id] = [];
            if (Read(Header(16) + id, 1) != 0)
            {
                var count = (int)Read(lists, AssignableCountBits);
                lists += AssignableCountBits;
                assignable[id] = [.. Enumerable.Range(0, count).Select(index => (int)Read(lists + (long)index * IdBits, IdBits))];
                lists += (long)count * IdBits;
            }
        }
        return (bases, assignable);
    }

    // The IDs in the type's own extra assignable list whose implemented bits, past the last list, are set.
    internal int[] Interfaces(int id)
    {
        long offset = Header(16) + Capacity, own = -1, before = 0, entries = 0;
        var ownCount = 0;
        for (var type = 0; type < Capacity; type++)
        {
            if (Read(Header(16) + type, 1) == 0)
            {
                continue;
            }
            var count = (int)Read(offset, AssignableCountBits);
            if (type == id)
            {
                (own, ownCount, before) = (offset + AssignableCountBits, count, entries);
            }
            offset += AssignableCountBits + (long)count * IdBits;
            entries += count;
        }
        return [.. Enumerable.Range(0, ownCount).Where(index => Read(offset + before + index, 1) != 0)
            .Select(index => (int)Read(own + (long)index * IdBits, IdBits))];
    }

    internal int[]? GenericArguments(int id)
    {
        var section = Header(17);
        if (section == 0)
        {
            return null;
        }
        var presence = Dictionary(section, out var valueBits);
        if (Read(presence + id, 1) == 0)
        {
            return null;
        }
        var rank = Rank(presence, id);
        var samples = presence + Capacity + Blocks * RankBits;
        var lists = samples + (Header(18) + 7) / 8 * (long)SampleBits;
        var offset = lists + Read(samples + rank / 8 * (long)SampleBits, SampleBits);
        for (var skip = 0; skip < rank % 8; skip++)
        {
            offset += GenericCountBits + Read(offset, GenericCountBits) * (long)valueBits;
        }
        var count = (int)Read(offset, GenericCountBits);
        return [.. Enumerable.Range(0, count).Select(index =>
            Id(section, (int)Read(offset + GenericCountBits + (long)index * valueBits, valueBits)))];
    }

    internal int RelatedType(int id)
    {
        var section = Header(19);
        if (section == 0)
        {
            return 0;
        }
        var presence = Dictionary(section, out var valueBits);
        return Read(presence + id, 1) == 0
            ? 0
            : Id(section, (int)Read(presence + Capacity + Blocks * RankBits + Rank(presence, id) * (long)valueBits, valueBits));
    }

    // The size of the ID dictionary at the start of the section a header field locates; 0 means the
    // section's values are type IDs.
    internal int DictionarySize(int field) => (int)Read(Header(field), IdBits + 1);

    private long Dictionary(long section, out int valueBits)
    {
        var count = Read(section, IdBits + 1);
        valueBits = count == 0 ? IdBits : 32 - BitOperations.LeadingZeroCount(count - 1);
        return section + IdBits + 1 + count * (long)IdBits;
    }

    private int Id(long section, int value) => Read(section, IdBits + 1) == 0
        ? value
        : (int)Read(section + IdBits + 1 + (long)value * IdBits, IdBits);

    // Set presence bits before id: the directory's count for its block, then the block's own bits.
    private int Rank(long presence, int id)
    {
        var block = id / 256;
        var rank = (int)Read(presence + Capacity + block * (long)RankBits, RankBits);
        for (var bit = block * 256; bit < id; bit += 32)
        {
            var width = Math.Min(32, id - bit);
            rank += BitOperations.PopCount(Read(presence + bit, width));
        }
        return rank;
    }

    internal uint Read(long offset, int width)
    {
        if (width == 0)
        {
            return 0;
        }
        var word = BinaryPrimitives.ReadUInt64LittleEndian(table.AsSpan((int)(offset >> 3), 8));
        return (uint)(word >> (int)(offset & 7) & (width == 32 ? uint.MaxValue : (1UL << width) - 1));
    }
}
