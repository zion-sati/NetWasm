using System.Collections.Immutable;
using NetWasm.Compiler.Layout;

namespace NetWasm.Compiler.Tests.Layout;

public sealed class RuntimeTypeTableEncoderTests
{
    private const uint ValueTypeFlag = 1;
    private const uint InterfaceFlag = 1 << 2;
    private const uint SealedFlag = 1 << 11;
    private const uint ClassFlag = 1 << 10;

    [Fact]
    public void EveryFieldReadsBackAsTheRuntimeAndCoreLibReadIt()
    {
        RuntimeTypeTableEntry[] entries =
        [
            Entry(1, ClassFlag, 8, [], 0, [1]),
            Entry(2, ClassFlag | (18u << 24), 16, [8], 1, [1, 2]) with { HasFinalizer = true },
            Entry(3, ValueTypeFlag | (9u << 24), 16, [], 1, [3]) with
            {
                Value = new RuntimeValueLayout(4, 8, []),
            },
            Entry(4, ValueTypeFlag, 24, [8, 16], 1, [4]) with
            {
                Value = new RuntimeValueLayout(16, 8, [0, 8]),
                RelatedTypeId = 2,
                GenericArgumentTypeIds = [3, 2],
            },
            Entry(5, InterfaceFlag, 0, [], 0, [5]),
        ];

        var reader = Read(entries, generic: true, declaring: true);

        Assert.Equal(1u, reader.Header(0));
        Assert.Equal(6, reader.Capacity);
        Assert.Equal(0u, reader.FlagWord(0));
        Assert.Equal(ClassFlag | (18u << 24) | RuntimeTypeTableEncoder.FinalizerFlag | RuntimeTypeTableEncoder.RegisteredFlag,
            reader.FlagWord(2));
        Assert.Equal(ValueTypeFlag | RuntimeTypeTableEncoder.ValueLayoutFlag | RuntimeTypeTableEncoder.RegisteredFlag,
            reader.FlagWord(4));
        AssertLayout(reader.Layout(2), 16, [false, true], 0, 0, []);
        AssertLayout(reader.Layout(3), 16, [], 4, 8, []);
        AssertLayout(reader.Layout(4), 24, [false, true, true], 16, 8, [true, true]);
        var (bases, _) = reader.DecodeRegistry();
        Assert.Equal([0, 0, 1, 1, 1, 0], bases);
        Assert.Equal([3, 2], reader.GenericArguments(4)!);
        Assert.Null(reader.GenericArguments(2));
        Assert.Equal(2, reader.RelatedType(4));
        Assert.Equal(0, reader.RelatedType(3));
    }

    // A cast walks the base chain and each level's list; the table keeps only what that walk would
    // not otherwise find, so every pair answers as it did with the full lists.
    [Fact]
    public void AssignabilityAnswersAsTheFullListsDid()
    {
        // 1 object; 2 IFoo; 3 IBar : IFoo; 4 Base : IBar; 5 Derived : Base, IBaz(6); 7 Leaf : Derived.
        var full = new Dictionary<int, (int Base, int[] Assignable)>
        {
            [1] = (0, [1]),
            [2] = (0, [2]),
            [3] = (0, [2, 3]),
            [4] = (1, [1, 2, 3, 4]),
            [5] = (4, [1, 2, 3, 4, 5, 6]),
            [6] = (0, [6]),
            [7] = (5, [1, 2, 3, 4, 5, 6, 7]),
        };
        var entries = full.Select(pair => Entry(pair.Key,
            pair.Key is 2 or 3 or 6 ? InterfaceFlag : ClassFlag, 8, [], pair.Value.Base, [.. pair.Value.Assignable])).ToArray();

        var (bases, extra) = Read(entries, generic: false, declaring: false).DecodeRegistry();

        foreach (var actual in full.Keys)
        {
            foreach (var target in full.Keys)
            {
                Assert.Equal(Walk(actual, target, id => full[id].Base, id => full[id].Assignable),
                    Walk(actual, target, id => bases[id], id => extra[id]));
            }
        }
        Assert.Empty(extra[7]);
        Assert.Equal([6], extra[5]);
    }

    [Fact]
    public void RanksAndSamplesLocateListsPastTheirFirstBlockAndSample()
    {
        var entries = Enumerable.Range(1, 700).Select(id => Entry(id, ClassFlag, 8, [], 0, []) with
        {
            GenericArgumentTypeIds = id % 3 == 0 ? [.. Enumerable.Range(1, id % 5 + 1)] : [],
            RelatedTypeId = id % 7 == 0 ? id - 1 : 0,
        }).ToArray();

        var reader = Read(entries, generic: true, declaring: true);

        foreach (var entry in entries)
        {
            Assert.Equal(entry.GenericArgumentTypeIds.IsEmpty ? null : [.. entry.GenericArgumentTypeIds],
                reader.GenericArguments(entry.TypeId));
            Assert.Equal(entry.RelatedTypeId, reader.RelatedType(entry.TypeId));
        }
    }

    [Fact]
    public void SectionsOnlyCoreLibReadsAreLeftOutUnlessAsked()
    {
        var entry = Entry(1, ClassFlag, 8, [], 0, []) with { GenericArgumentTypeIds = [1], RelatedTypeId = 1 };

        var reader = Read([entry], generic: false, declaring: false);

        Assert.Equal(0u, reader.Header(17));
        Assert.Equal(0u, reader.Header(19));
        Assert.True(Encode([entry], generic: true, declaring: true).Length >
            Encode([entry], generic: false, declaring: false).Length);
    }

    [Fact]
    public void IdenticalReferenceBitmapsShareOnePoolEntry()
    {
        RuntimeTypeTableEntry[] entries =
        [
            Entry(1, ClassFlag, 16, [8], 0, []),
            Entry(2, ClassFlag, 24, [8], 0, []),
            Entry(3, ClassFlag, 24, [16], 0, []),
            Entry(4, ClassFlag, 32, [], 0, []),
        ];

        var reader = Read(entries, generic: false, declaring: false);

        Assert.Equal(2u, reader.Header(12));
        Assert.Equal(reader.Layout(1).Bitmap, reader.Layout(2).Bitmap);
        Assert.Empty(reader.Layout(4).Bitmap);
    }

    [Fact]
    public void AnEmptyTableHasOnlyItsHeaderAndPadding()
    {
        var reader = Read([], generic: false, declaring: false);

        Assert.Equal(1, reader.Capacity);
        Assert.Equal(0u, reader.FlagWord(0));
    }

    [Fact]
    public void RejectsWhatTheFormatCannotHold()
    {
        var encoder = new RuntimeTypeTableEncoder();
        var valid = Entry(1, ClassFlag, 8, [], 0, []);

        Assert.Throws<ArgumentNullException>(() => encoder.Encode(null!));
        Assert.Throws<ArgumentException>(() => encoder.Encode(new(default, 8, false, false, false)));
        Assert.Throws<ArgumentException>(() => encoder.Encode(new([valid], 2, false, false, false)));
        Assert.Throws<ArgumentException>(() => encoder.Encode(new([valid, valid], 8, false, false, false)));
        Assert.Throws<ArgumentException>(() => encoder.Encode(new([valid with { TypeId = 0 }], 8, false, false, false)));
        Assert.Throws<ArgumentException>(() => encoder.Encode(new([valid with { Flags = RuntimeTypeTableEncoder.FinalizerFlag }], 8, false, false, false)));
        Assert.Throws<ArgumentException>(() => encoder.Encode(new([valid with { BaseTypeId = 2 }], 8, false, false, false)));
        Assert.Throws<ArgumentException>(() => encoder.Encode(new([valid with { RelatedTypeId = 9 }], 8, false, false, false)));
        Assert.Throws<ArgumentException>(() => encoder.Encode(new([valid with { AssignableTypeIds = [3] }], 8, false, false, false)));
        Assert.Throws<ArgumentException>(() => encoder.Encode(new([valid with { ReferenceOffsets = [4] }], 8, false, false, false)));
        Assert.Throws<ArgumentException>(() => encoder.Encode(new([valid with { ReferenceOffsets = [8] }], 8, false, false, false)));
        Assert.Throws<ArgumentException>(() => encoder.Encode(new([valid with { GenericDefinitionTypeId = 2 }], 8, false, false, false)));
    }

    // A type's record is an index into the distinct shapes: its flag word, layout and, when the
    // program reads definitions, generic definition. Types alike share one.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TypesAlikeShareOneShapeWithTheirDefinition(bool definitions)
    {
        RuntimeTypeTableEntry[] entries =
        [
            Entry(1, ClassFlag, 8, [], 0, []),
            Entry(2, ClassFlag, 8, [], 1, []) with { GenericArgumentTypeIds = [3] },
            Entry(3, ClassFlag, 0, [], 0, []),
            Entry(4, ClassFlag, 8, [], 1, []) with { GenericArgumentTypeIds = [1], GenericDefinitionTypeId = 2 },
            Entry(5, ClassFlag, 8, [], 1, []) with { GenericArgumentTypeIds = [4], GenericDefinitionTypeId = 2 },
            Entry(6, ClassFlag, 8, [], 1, []) with { GenericArgumentTypeIds = [4, 1], GenericDefinitionTypeId = 7 },
            Entry(7, ClassFlag, 8, [], 1, []),
        ];

        var reader = new RuntimeTypeTableReader([.. new RuntimeTypeTableEncoder().Encode(new([.. entries], 8, true, false, definitions))]);

        Assert.Equal(definitions ? [0, 0, 0, 2, 2, 7, 0] : [0, 0, 0, 0, 0, 0, 0],
            entries.Select(entry => reader.GenericDefinition(entry.TypeId)));
        Assert.Equal(definitions ? 5 : 3, reader.Shapes);
        Assert.All(entries, entry => Assert.Equal(ClassFlag | RuntimeTypeTableEncoder.RegisteredFlag, reader.FlagWord(entry.TypeId)));
        Assert.Equal([4, 1], reader.GenericArguments(6)!);
        Assert.Equal(0, reader.Layout(3).Size);
        Assert.Equal(8, reader.Layout(5).Size);
    }

    // With assemblies, a section holds the entry assembly's index and one run per stretch of registered type IDs
    // in one assembly, an unused ID between them not breaking a run; the shapes stay as they are without it.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AssemblyRunsCoverConsecutiveTypesOfOneAssembly(bool assemblies)
    {
        RuntimeTypeTableEntry[] entries =
        [
            Entry(1, ClassFlag, 8, [], 0, []) with { AssemblyIndex = 1 },
            Entry(2, ClassFlag, 8, [], 1, []) with { AssemblyIndex = 1, GenericDefinitionTypeId = 4 },
            Entry(4, ClassFlag, 8, [], 1, []) with { AssemblyIndex = 1 },
            Entry(5, ClassFlag, 8, [], 1, []) with { AssemblyIndex = 2 },
            Entry(6, ClassFlag, 8, [], 1, []) with { AssemblyIndex = 3 },
            Entry(7, ClassFlag, 8, [], 1, []) with { AssemblyIndex = 3 },
            Entry(8, ClassFlag, 8, [], 1, []) with { AssemblyIndex = 1 },
        ];

        var reader = new RuntimeTypeTableReader([.. new RuntimeTypeTableEncoder().Encode(
            new([.. entries], 8, false, false, true) { IncludeAssemblies = assemblies, EntryAssemblyIndex = assemblies ? 2 : 0 })]);

        Assert.Equal(assemblies ? [1, 1, 1, 2, 3, 3, 1] : [0, 0, 0, 0, 0, 0, 0],
            entries.Select(entry => reader.Assembly(entry.TypeId)));
        Assert.Equal(assemblies ? 4 : 0, reader.AssemblyRuns);
        Assert.Equal(assemblies ? 2 : 0, reader.AssemblyBits);
        Assert.Equal([0, 4, 0, 0, 0, 0, 0], entries.Select(entry => reader.GenericDefinition(entry.TypeId)));
        Assert.Equal(assemblies ? 2 : 0, reader.EntryAssembly);
        Assert.Equal(3, reader.Shapes);
        Assert.All(entries, entry => Assert.Equal(8, reader.Layout(entry.TypeId).Size));
    }

    // With TypeAttributes, the distinct values follow the flag words, without the bits the flag words
    // hold, and each shape indexes its type's; types that differ only in their attributes take their
    // own shapes. The bases still read past them.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ShapesIndexTypeAttributesWhenAsked(bool attributes)
    {
        RuntimeTypeTableEntry[] entries =
        [
            Entry(1, ClassFlag, 8, [], 0, []) with { TypeAttributes = 0x100001 },
            Entry(2, ClassFlag, 8, [], 1, []) with { TypeAttributes = 0x100001 },
            Entry(3, ClassFlag | SealedFlag, 8, [], 1, []) with { TypeAttributes = 0x2101 },
            Entry(4, ClassFlag, 8, [], 1, []) with { TypeAttributes = 0x1 },
        ];

        var reader = new RuntimeTypeTableReader([.. new RuntimeTypeTableEncoder().Encode(
            new([.. entries], 8, false, false, false) { IncludeTypeAttributes = attributes })]);

        Assert.Equal(attributes ? 2 : 0, reader.AttributeBits);
        Assert.Equal(attributes ? 4 : 3, reader.Shapes);
        if (attributes)
        {
            Assert.Equal([0x100001, 0x100001, 0x2101, 0x1], entries.Select(entry => reader.Attributes(entry.TypeId)));
        }
        Assert.Equal([0, 1, 1, 1], reader.DecodeRegistry().Bases[1..]);
    }

    // With interfaces, a bit per listed assignable ID says the type implements it, and a type keeps an
    // interface it implements that its base reaches only through variance; without, the base hides it.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AssignableListsMarkTheInterfacesATypeImplements(bool interfaces)
    {
        RuntimeTypeTableEntry[] entries =
        [
            Entry(1, ClassFlag, 8, [], 0, [5]),
            Entry(2, ClassFlag, 8, [], 1, [5, 6]) with { ImplementedInterfaceTypeIds = [5, 6] },
            Entry(3, ClassFlag, 8, [], 0, [6]),
            Entry(5, InterfaceFlag, 0, [], 0, []),
            Entry(6, InterfaceFlag, 0, [], 0, []),
        ];

        var reader = new RuntimeTypeTableReader([.. new RuntimeTypeTableEncoder().Encode(
            new([.. entries], 8, false, false, false) { IncludeInterfaces = interfaces })]);

        Assert.Equal(interfaces ? [5, 6] : [6], reader.DecodeRegistry().Assignable[2]);
        if (interfaces)
        {
            Assert.Equal([5, 6], reader.Interfaces(2));
            Assert.Empty(reader.Interfaces(1));
            Assert.Empty(reader.Interfaces(3));
            Assert.Empty(reader.Interfaces(4));
        }
    }

    [Fact]
    public void RejectsAssemblyIndexesTheTableCannotHold()
    {
        var encoder = new RuntimeTypeTableEncoder();
        var valid = Entry(1, ClassFlag, 8, [], 0, []) with { AssemblyIndex = 1 };

        Assert.Throws<ArgumentException>(() => encoder.Encode(new([valid with { AssemblyIndex = 0 }], 8, false, false, false)
        {
            IncludeAssemblies = true,
        }));
        Assert.Throws<ArgumentException>(() => encoder.Encode(new([valid with { AssemblyIndex = -1 }], 8, false, false, false)));
        Assert.Throws<ArgumentException>(() => encoder.Encode(new([valid], 8, false, false, false) { EntryAssemblyIndex = 1 }));
        Assert.Throws<ArgumentException>(() => encoder.Encode(new([valid], 8, false, false, false)
        {
            IncludeAssemblies = true,
            EntryAssemblyIndex = -1,
        }));
    }

    // A section's IDs are indexes into its distinct IDs where that is smaller, and type IDs where not.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RepeatedIdsGoThroughADictionary(bool repeated)
    {
        var entries = Enumerable.Range(1, 64).Select(id => Entry(id, ClassFlag, 8, [], 0, []) with
        {
            GenericArgumentTypeIds = id > 2 ? [repeated ? id % 2 + 1 : id - 1, repeated ? 1 : id - 2] : [],
            RelatedTypeId = id > 1 ? (repeated ? 1 : id - 1) : 0,
        }).ToArray();

        var reader = Read(entries, generic: true, declaring: true);

        Assert.Equal(repeated ? 2 : 0, reader.DictionarySize(17));
        Assert.Equal(repeated ? 1 : 0, reader.DictionarySize(19));
        foreach (var entry in entries)
        {
            Assert.Equal(entry.GenericArgumentTypeIds.IsEmpty ? null : [.. entry.GenericArgumentTypeIds],
                reader.GenericArguments(entry.TypeId));
            Assert.Equal(entry.RelatedTypeId, reader.RelatedType(entry.TypeId));
        }
    }

    [Fact]
    public void BitWriterPatchesFieldsAndRejectsValuesTooWide()
    {
        var writer = new RuntimeTypeTableBitWriter();
        writer.Write(0b101, 3);
        writer.Write(0, 32);
        writer.Patch(3, 0xF00Du, 16);
        writer.Patch(0, 0b010, 3);

        var reader = new RuntimeTypeTableReader([.. writer.ToBytes(8)]);

        Assert.Equal(0b010u, reader.Read(0, 3));
        Assert.Equal(0xF00Du, reader.Read(3, 16));
        Assert.Equal(0, RuntimeTypeTableBitWriter.Width(0));
        Assert.Equal(1, RuntimeTypeTableBitWriter.Width(1));
        Assert.Equal(10, RuntimeTypeTableBitWriter.Width(816));
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.Write(8, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.Write(0, 33));
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.Patch(30, 0, 8));
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.Patch(-1, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.ToBytes(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => RuntimeTypeTableBitWriter.Width(-1));
    }

    private static int Walk(int actual, int target, Func<int, int> baseOf, Func<int, int[]> listOf)
    {
        for (; actual != 0; actual = baseOf(actual))
        {
            if (actual == target || listOf(actual).Contains(target))
            {
                return 1;
            }
        }
        return 0;
    }

    private static void AssertLayout(
        (int Size, bool[] Bitmap, int ValueSize, int Payload, bool[] ValueBitmap) layout,
        int size, bool[] bitmap, int valueSize, int payload, bool[] valueBitmap)
    {
        Assert.Equal(size, layout.Size);
        Assert.Equal(bitmap, layout.Bitmap);
        Assert.Equal(valueSize, layout.ValueSize);
        Assert.Equal(payload, layout.Payload);
        Assert.Equal(valueBitmap, layout.ValueBitmap);
    }

    private static RuntimeTypeTableEntry Entry(int id, uint flags, int size, int[] references, int baseType, int[] assignable) =>
        new(id, flags, size, [.. references], baseType, [.. assignable]);

    private static RuntimeTypeTableReader Read(RuntimeTypeTableEntry[] entries, bool generic, bool declaring) =>
        new([.. Encode(entries, generic, declaring)]);

    private static ImmutableArray<byte> Encode(RuntimeTypeTableEntry[] entries, bool generic, bool declaring) =>
        new RuntimeTypeTableEncoder().Encode(new([.. entries], 8, generic, declaring, false));
}
