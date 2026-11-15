// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Buffers.Binary;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Layout;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

public sealed class EnumMetadataBuilderTests
{
    private static readonly ImmutableHashSet<EnumMetadataRequirement> AllRequirements =
        [new(null, EnumMetadataPayload.Names | EnumMetadataPayload.Values |
            EnumMetadataPayload.RuntimeDescriptor)];

    [Theory]
    [InlineData(WasmTarget.Wasm32, EnumMetadataPayload.None)]
    [InlineData(WasmTarget.Wasm64, EnumMetadataPayload.None)]
    [InlineData(WasmTarget.Wasm32, EnumMetadataPayload.Values)]
    [InlineData(WasmTarget.Wasm64, EnumMetadataPayload.Values)]
    [InlineData(WasmTarget.Wasm32, EnumMetadataPayload.Names)]
    [InlineData(WasmTarget.Wasm64, EnumMetadataPayload.Names)]
    [InlineData(WasmTarget.Wasm32, EnumMetadataPayload.Values | EnumMetadataPayload.Names)]
    [InlineData(WasmTarget.Wasm64, EnumMetadataPayload.Values | EnumMetadataPayload.Names)]
    public void KeepsCompilerMembersWithoutSerializingRuntimeDescriptors(
        WasmTarget target, EnumMetadataPayload payload)
    {
        var assembly = new AssemblyIdentity("EnumPayloadTests");
        var key = new EntityKey(assembly, 1);
        var definition = EnumDefinition(key, "Value", Primitive("u8", CliValueKind.I8),
            [new("High", 1UL << 63), new("Low", 1)]);
        var state = new ManagedStaticDataBuildState { Cursor = 19 };
        if ((payload & EnumMetadataPayload.Names) != 0)
        {
            AddNames(state, definition.EnumMembers, 100);
        }
        var collector = As<IEnumMetadataCollector>(CreateCollector(
            new Repository([definition]), Layouts((key, 7)),
            [new(key, payload)], state));
        var builder = As<IEnumMetadataBuilder>(new EnumMetadataBuilder(
            state, WasmTargetLayout.For(target)));

        collector.Collect();
        builder.Build();

        var metadata = Assert.Single(state.EnumMetadata);
        Assert.Equal(key, metadata.Type);
        Assert.Equal(7, metadata.TypeId);
        Assert.Equal(definition.EnumUnderlyingType, metadata.UnderlyingType);
        Assert.Equal(0, metadata.Address);
        Assert.Empty(state.Segments);
        Assert.Equal(19, state.Cursor);
        if (payload == EnumMetadataPayload.None)
        {
            Assert.Empty(metadata.Members);
            return;
        }

        Assert.Equal([1UL, 1UL << 63], metadata.Members.Select(member => member.RawValue));
        Assert.Equal((payload & EnumMetadataPayload.Names) != 0 ? 120 : 0,
            metadata.Members[0].NameLayout.Address);
        Assert.Equal((payload & EnumMetadataPayload.Names) != 0 ? 100 : 0,
            metadata.Members[1].NameLayout.Address);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, EnumMetadataPayload.None)]
    [InlineData(WasmTarget.Wasm64, EnumMetadataPayload.None)]
    [InlineData(WasmTarget.Wasm32, EnumMetadataPayload.Values)]
    [InlineData(WasmTarget.Wasm64, EnumMetadataPayload.Values)]
    [InlineData(WasmTarget.Wasm32,
        EnumMetadataPayload.Values | EnumMetadataPayload.RuntimeDescriptor)]
    [InlineData(WasmTarget.Wasm64,
        EnumMetadataPayload.Values | EnumMetadataPayload.RuntimeDescriptor)]
    public void PreservesOpenAndClosedEnumFactsWithoutCreatingValueStorage(
        WasmTarget target, EnumMetadataPayload payload)
    {
        var key = new EntityKey(new AssemblyIdentity("NestedEnumMetadataTests"), 1);
        var definition = EnumDefinition(key, "Outer`1+Code", Primitive("u2", CliValueKind.I4),
            [new("High", 3), new("Low", 1)]) with
        { GenericArity = 1 };
        var repository = new Repository([definition]);
        var named = repository.GetTypeIdentity(key);
        var intEnum = CliTypeIdentity.GenericInstantiation(named, [Primitive("i4", CliValueKind.I4)]);
        var stringEnum = CliTypeIdentity.GenericInstantiation(named,
            [CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference, false)]);
        var types = Layouts() with
        {
            Objects = new Dictionary<EntityKey, ObjectLayout>
            {
                [key] = new(2, 0, []),
            }.ToImmutableDictionary(),
            ConstructedObjects = new Dictionary<CliTypeIdentity, ObjectLayout>
            {
                [intEnum] = new(5, 8, []),
                [stringEnum] = new(8, 8, []),
            }.ToImmutableDictionary(),
        };
        var state = new ManagedStaticDataBuildState { Cursor = 19 };
        var collector = CreateCollector(repository, types, [new(key, payload)], state);
        var builder = As<IEnumMetadataBuilder>(
            new EnumMetadataBuilder(state, WasmTargetLayout.For(target)));

        collector.Collect();
        builder.Build();

        Assert.Equal([named, intEnum, stringEnum], state.EnumMetadata.Select(item => item.EnumType));
        Assert.Equal([2, 5, 8], state.EnumMetadata.Select(item => item.TypeId));
        Assert.Equal([true, false, false], state.EnumMetadata.Select(item => item.IsOpenDefinition));
        Assert.All(state.EnumMetadata, metadata =>
        {
            Assert.Equal(key, metadata.Type);
            Assert.Equal(definition.EnumUnderlyingType, metadata.UnderlyingType);
            Assert.False(metadata.IsFlags);
            if (payload == EnumMetadataPayload.None)
                Assert.Empty(metadata.Members);
            else
                Assert.Equal([1UL, 3UL], metadata.Members.Select(member => member.RawValue));
        });
        Assert.Empty(state.ValueTypeDescriptors);
        Assert.Equal(0, types.Objects[key].Size);
        if ((payload & EnumMetadataPayload.RuntimeDescriptor) == 0)
        {
            Assert.All(state.EnumMetadata, metadata => Assert.Equal(0, metadata.Address));
            Assert.Empty(state.Segments);
            Assert.Equal(19, state.Cursor);
        }
        else
        {
            Assert.Equal(3, state.Segments.Count);
            Assert.Equal(3, state.EnumMetadata.Select(item => item.Address).Distinct().Count());
            Assert.All(state.Segments, segment =>
            {
                Assert.Equal(4, ReadInt32(segment.Data, 4));
                Assert.Equal(2, ReadInt32(segment.Data, 12));
            });
        }
    }

    [Fact]
    public void ConstructionIsSideEffectFreeUntilMetadataIsCollected()
    {
        var assembly = new AssemblyIdentity("EnumMetadataConstructionTests");
        var key = new EntityKey(assembly, 1);
        var state = new ManagedStaticDataBuildState();
        var collector = As<IEnumMetadataCollector>(CreateCollector(
            new Repository([EnumDefinition(
                key,
                "Value",
                Primitive("i4", CliValueKind.I4),
                [])]),
            Layouts((key, 7)),
            AllRequirements,
            state));

        Assert.Empty(state.PendingEnumMetadata);

        collector.Collect();

        Assert.Equal(key, Assert.Single(state.PendingEnumMetadata).Value.Type);
    }

    [Fact]
    public void BuildsOrderedFlagMetadataAndPreservesAliases()
    {
        var assembly = new AssemblyIdentity("EnumMetadataBuilderTests");
        var plainKey = new EntityKey(assembly, 1);
        var laterKey = new EntityKey(assembly, 2);
        var earlierKey = new EntityKey(assembly, 3);
        var plain = new TypeDefinitionModel(
            plainKey, "Tests", "Plain", true, [], []);
        var later = EnumDefinition(
            laterKey,
            "Later",
            Primitive("i4", CliValueKind.I4),
            [
                new("Two", 2),
                new("AliasOne", 1),
                new("One", 1),
                new("None", 0),
            ],
            flags: true);
        var earlier = EnumDefinition(
            earlierKey,
            "Earlier",
            Primitive("u1", CliValueKind.I4),
            [new("Zero", 0), new("Max", byte.MaxValue)]);
        var types = Layouts(
            (plainKey, 20),
            (laterKey, 11),
            (earlierKey, 7));
        var state = new ManagedStaticDataBuildState { Cursor = 17 };
        AddNames(state, later.EnumMembers, 100);
        AddNames(state, earlier.EnumMembers, 200);

        CollectAndBuild(
            new Repository([plain, later, earlier]),
            types,
            state,
            WasmTargetLayout.Wasm32);

        Assert.Equal(
            [earlierKey, laterKey],
            state.EnumMetadata.Select(metadata => metadata.Type));
        var metadata = state.EnumMetadata.Single(item => item.Type == laterKey);
        Assert.Equal(11, metadata.TypeId);
        Assert.Equal(Primitive("i4", CliValueKind.I4), metadata.UnderlyingType);
        Assert.True(metadata.IsFlags);
        Assert.Equal(4, metadata.Members.Length);
        Assert.Equal("None", metadata.Members[0].Name);
        Assert.Equal("AliasOne", metadata.Members[1].Name);
        Assert.Equal("One", metadata.Members[2].Name);
        Assert.Equal("Two", metadata.Members[3].Name);
        Assert.Equal(1UL, metadata.Members[1].RawValue);
        Assert.Equal(1UL, metadata.Members[2].RawValue);

        var segment = state.Segments.Single(item => item.Address == metadata.Address);
        Assert.Equal(1, ReadInt32(segment.Data, 0));
        Assert.Equal(5, ReadInt32(segment.Data, 4));
        Assert.Equal(7, ReadInt32(segment.Data, 8));
        Assert.Equal(4, ReadInt32(segment.Data, 12));
        Assert.Equal(0UL, ReadUInt64(segment.Data, 24));
        Assert.Equal(1UL, ReadUInt64(segment.Data, 32));
        Assert.Equal(1UL, ReadUInt64(segment.Data, 40));
        Assert.Equal(2UL, ReadUInt64(segment.Data, 48));
        Assert.Equal(24, state.EnumMetadata[0].Address);
    }

    [Fact]
    public void OrdersSignedEnumByUnsignedStorageBitsAcrossWidths()
    {
        var assembly = new AssemblyIdentity("EnumMetadataOrderingTests");
        var intKey = new EntityKey(assembly, 1);
        var longKey = new EntityKey(assembly, 2);
        var intDefinition = EnumDefinition(
            intKey,
            "SignedInt",
            Primitive("i4", CliValueKind.I4),
            [
                new("IntNegative", uint.MaxValue),
                new("IntOne", 1),
                new("IntZero", 0),
            ]);
        var longDefinition = EnumDefinition(
            longKey,
            "SignedLong",
            Primitive("i8", CliValueKind.I8),
            [
                new("LongNegative", ulong.MaxValue),
                new("LongOne", 1),
                new("LongZero", 0),
            ]);
        var state = new ManagedStaticDataBuildState();
        AddNames(state, intDefinition.EnumMembers, 100);
        AddNames(state, longDefinition.EnumMembers, 200);

        CollectAndBuild(
            new Repository([intDefinition, longDefinition]),
            Layouts((intKey, 1), (longKey, 2)),
            state,
            WasmTargetLayout.Wasm32);

        Assert.Equal(
            ["IntZero", "IntOne", "IntNegative"],
            state.EnumMetadata.Single(metadata => metadata.Type == intKey)
                .Members.Select(member => member.Name));
        Assert.Equal(
            ["LongZero", "LongOne", "LongNegative"],
            state.EnumMetadata.Single(metadata => metadata.Type == longKey)
                .Members.Select(member => member.Name));
    }

    [Theory]
    [InlineData("i1", CliValueKind.I4, 1)]
    [InlineData("u1", CliValueKind.I4, 2)]
    [InlineData("i2", CliValueKind.I4, 3)]
    [InlineData("u2", CliValueKind.I4, 4)]
    [InlineData("i4", CliValueKind.I4, 5)]
    [InlineData("u4", CliValueKind.I4, 6)]
    [InlineData("i8", CliValueKind.I8, 7)]
    [InlineData("u8", CliValueKind.I8, 8)]
    [InlineData("char", CliValueKind.I4, 9)]
    public void WritesEverySupportedUnderlyingTypeCode(
        string name,
        CliValueKind stackKind,
        int expectedCode)
    {
        var assembly = new AssemblyIdentity("EnumMetadataWidthTests");
        var key = new EntityKey(assembly, expectedCode);
        var definition = EnumDefinition(
            key,
            name,
            Primitive(name, stackKind),
            []);
        var state = new ManagedStaticDataBuildState();

        CollectAndBuild(
            new Repository([definition]),
            Layouts((key, 40 + expectedCode)),
            state,
            WasmTargetLayout.Wasm32);

        var metadata = Assert.Single(state.EnumMetadata);
        var segment = Assert.Single(state.Segments);
        Assert.Equal(expectedCode, ReadInt32(segment.Data, 4));
        Assert.Equal(1, ReadInt32(segment.Data, 0));
        Assert.Equal(24, segment.Data.Length);
        Assert.Equal(metadata.Address, segment.Address);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void WritesVersionedRuntimeDescriptorWithTargetWidthPointers(WasmTarget target)
    {
        var assembly = new AssemblyIdentity("EnumDescriptorTests");
        var key = new EntityKey(assembly, 1);
        var definition = EnumDefinition(
            key,
            "Permissions",
            Primitive("u4", CliValueKind.I4),
            [new("None", 0), new("Read", 1)],
            flags: true);
        var state = new ManagedStaticDataBuildState { Cursor = 19 };
        AddNames(state, definition.EnumMembers, 100);
        var layout = WasmTargetLayout.For(target);

        CollectAndBuild(new Repository([definition]), Layouts((key, 7)), state, layout);

        var metadata = Assert.Single(state.EnumMetadata);
        var segment = Assert.Single(state.Segments);
        var headerSize = 16 + (2 * layout.AddressSize);
        var valuesAddress = metadata.Address + headerSize;
        var namesAddress = valuesAddress + 16;
        Assert.Equal(0, metadata.Address % 8);
        Assert.Equal(1, ReadInt32(segment.Data, 0));
        Assert.Equal(6, ReadInt32(segment.Data, 4));
        Assert.Equal(7, ReadInt32(segment.Data, 8));
        Assert.Equal(2, ReadInt32(segment.Data, 12));
        Assert.Equal(valuesAddress, ReadAddress(segment.Data, 16, layout.AddressSize));
        Assert.Equal(namesAddress,
            ReadAddress(segment.Data, 16 + layout.AddressSize, layout.AddressSize));
        Assert.Equal(0UL, ReadUInt64(segment.Data, headerSize));
        Assert.Equal(1UL, ReadUInt64(segment.Data, headerSize + 8));
        Assert.Equal(100, ReadAddress(segment.Data, headerSize + 16, layout.AddressSize));
        Assert.Equal(120,
            ReadAddress(segment.Data, headerSize + 16 + layout.AddressSize, layout.AddressSize));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, EnumMetadataPayload.RuntimeDescriptor)]
    [InlineData(WasmTarget.Wasm64, EnumMetadataPayload.RuntimeDescriptor)]
    [InlineData(WasmTarget.Wasm32,
        EnumMetadataPayload.RuntimeDescriptor | EnumMetadataPayload.Values)]
    [InlineData(WasmTarget.Wasm64,
        EnumMetadataPayload.RuntimeDescriptor | EnumMetadataPayload.Values)]
    [InlineData(WasmTarget.Wasm32,
        EnumMetadataPayload.RuntimeDescriptor | EnumMetadataPayload.Names)]
    [InlineData(WasmTarget.Wasm64,
        EnumMetadataPayload.RuntimeDescriptor | EnumMetadataPayload.Names)]
    public void RuntimeDescriptorKeepsOptionalPayloadsIndependent(
        WasmTarget target, EnumMetadataPayload payload)
    {
        var assembly = new AssemblyIdentity("PartialEnumDescriptorTests");
        var key = new EntityKey(assembly, 1);
        var definition = EnumDefinition(
            key,
            "Permissions",
            Primitive("u4", CliValueKind.I4),
            [new("None", 0), new("Read", 1)],
            flags: true);
        var state = new ManagedStaticDataBuildState { Cursor = 19 };
        if ((payload & EnumMetadataPayload.Names) != 0)
            AddNames(state, definition.EnumMembers, 100);
        var layout = WasmTargetLayout.For(target);
        var collector = As<IEnumMetadataCollector>(CreateCollector(
            new Repository([definition]), Layouts((key, 7)),
            [new(key, payload)], state));

        collector.Collect();
        new EnumMetadataBuilder(state, layout).Build();

        var metadata = Assert.Single(state.EnumMetadata);
        var segment = Assert.Single(state.Segments);
        var hasValues = (payload & EnumMetadataPayload.Values) != 0;
        var hasNames = (payload & EnumMetadataPayload.Names) != 0;
        Assert.Equal(1 | (hasValues ? 2 : 0) | (hasNames ? 4 : 0),
            ReadInt32(segment.Data, 8));
        Assert.Equal(
            hasValues ? metadata.Address + 16 + (2 * layout.AddressSize) : 0,
            ReadAddress(segment.Data, 16, layout.AddressSize));
        Assert.Equal(
            hasNames
                ? metadata.Address + 16 + (2 * layout.AddressSize) +
                  (hasValues ? 16 : 0)
                : 0,
            ReadAddress(segment.Data, 16 + layout.AddressSize, layout.AddressSize));
    }

    [Fact]
    public void SkipsNonEnumTypesAndLeavesEmptyStateUntouched()
    {
        var assembly = new AssemblyIdentity("EnumMetadataEmptyTests");
        var key = new EntityKey(assembly, 1);
        var state = new ManagedStaticDataBuildState { Cursor = 23 };

        CollectAndBuild(
            new Repository([
                new TypeDefinitionModel(key, "Tests", "Plain", true, [], []),
            ]),
            Layouts((key, 4)),
            state,
            WasmTargetLayout.Wasm64);

        Assert.Empty(state.PendingEnumMetadata);
        Assert.Empty(state.EnumMetadata);
        Assert.Empty(state.Segments);
        Assert.Equal(23, state.Cursor);
    }

    [Fact]
    public void RejectsMissingEnumMemberNameStorage()
    {
        var assembly = new AssemblyIdentity("EnumMetadataFailureTests");
        var key = new EntityKey(assembly, 1);
        var definition = EnumDefinition(
            key,
            "MissingName",
            Primitive("i4", CliValueKind.I4),
            [new("Present", 1), new("Missing", 2)]);
        var state = new ManagedStaticDataBuildState();
        state.Strings.Add("Present", new StringLayout(100, 7, 8));

        CreateCollector(
            new Repository([definition]),
            Layouts((key, 5)),
            AllRequirements,
            state).Collect();
        var exception = Assert.Throws<CompilerException>(() =>
            new EnumMetadataBuilder(state, WasmTargetLayout.Wasm32).Build());

        Assert.Equal(DiagnosticCode.RuntimeContract, exception.Diagnostic.Code);
        Assert.Contains("Missing", exception.Message);
        Assert.Empty(state.EnumMetadata);
        Assert.Empty(state.Segments);
    }

    [Fact]
    public void RejectsUnsupportedUnderlyingType()
    {
        var assembly = new AssemblyIdentity("EnumMetadataFailureTests");
        var key = new EntityKey(assembly, 1);
        var definition = EnumDefinition(
            key,
            "Floating",
            Primitive("f4", CliValueKind.F4),
            []);

        var state = new ManagedStaticDataBuildState();
        CreateCollector(
            new Repository([definition]),
            Layouts((key, 5)),
            AllRequirements,
            state).Collect();
        var exception = Assert.Throws<CompilerException>(() =>
            new EnumMetadataBuilder(state, WasmTargetLayout.Wasm32).Build());

        Assert.Equal(DiagnosticCode.UnsupportedMetadata, exception.Diagnostic.Code);
        Assert.Contains("primitive:f4", exception.Message);
    }

    [Fact]
    public void RejectsNullBuilderDependencies()
    {
        var target = WasmTargetLayout.Wasm32;
        var types = Layouts();
        var state = new ManagedStaticDataBuildState();
        var repository = new Repository([]);

        Assert.Throws<ArgumentNullException>(() => new EnumMetadataCollector(
            null!, repository, types, AllRequirements, state));
        Assert.Throws<ArgumentNullException>(() => new EnumMetadataCollector(
            repository, null!, types, AllRequirements, state));
        Assert.Throws<ArgumentNullException>(() => new EnumMetadataCollector(
            repository, repository, null!, AllRequirements, state));
        Assert.Throws<ArgumentNullException>(() => new EnumMetadataCollector(
            repository, repository, types, AllRequirements, null!));
        Assert.Throws<ArgumentNullException>(() => new EnumMetadataCollector(
            repository, repository, types, null!, state));
        Assert.Throws<ArgumentNullException>(() => new EnumMetadataBuilder(null!, target));
        Assert.Throws<ArgumentNullException>(() => new EnumMetadataBuilder(state, null!));
    }

    private static TypeDefinitionModel EnumDefinition(
        EntityKey key,
        string name,
        CliTypeIdentity underlying,
        ImmutableArray<EnumMemberModel> members,
        bool flags = false) =>
        new(key, "Tests", name, true, [], [])
        {
            IsEnum = true,
            EnumUnderlyingType = underlying,
            EnumMembers = members,
            IsFlagsEnum = flags,
        };

    private static CliTypeIdentity Primitive(string name, CliValueKind stackKind) =>
        CliTypeIdentity.Primitive(name, stackKind);

    private static T As<T>(T value) where T : class =>
        new[] { value }.Cast<T>().Single();

    private static IEnumMetadataCollector CreateCollector(
        Repository repository,
        ManagedTypeLayouts types,
        ImmutableHashSet<EnumMetadataRequirement> requirements,
        ManagedStaticDataBuildState state) =>
        As<IEnumMetadataCollector>(
            new EnumMetadataCollector(repository, repository, types, requirements, state));

    private static void CollectAndBuild(
        Repository repository,
        ManagedTypeLayouts types,
        ManagedStaticDataBuildState state,
        WasmTargetLayout target)
    {
        CreateCollector(repository, types, AllRequirements, state).Collect();
        new EnumMetadataBuilder(state, target).Build();
    }

    private static ManagedTypeLayouts Layouts(
        params (EntityKey Key, int TypeId)[] entries)
    {
        var objects = entries.ToImmutableDictionary(
            entry => entry.Key,
            entry => new ObjectLayout(entry.TypeId, 8, []));
        return new ManagedTypeLayouts(
            objects,
            ImmutableDictionary<CliTypeIdentity, ObjectLayout>.Empty,
            ImmutableDictionary<string, ObjectLayout>.Empty,
            ImmutableDictionary<CliTypeIdentity, ObjectLayout>.Empty,
            new ManagedValueLayoutState(),
            WasmTargetLayout.Wasm32,
            -1,
            -1,
            -1,
            -1);
    }

    private static void AddNames(
        ManagedStaticDataBuildState state,
        IEnumerable<EnumMemberModel> members,
        int firstAddress)
    {
        var address = firstAddress;
        foreach (var member in members)
        {
            state.Strings.Add(member.Name, new StringLayout(address, member.Name.Length, 8));
            address += 20;
        }
    }

    private static int ReadInt32(ImmutableArray<byte> bytes, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan().Slice(offset));

    private static ulong ReadUInt64(ImmutableArray<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan().Slice(offset));

    private static long ReadAddress(
        ImmutableArray<byte> bytes, int offset, int addressSize) =>
        addressSize == sizeof(int)
            ? ReadInt32(bytes, offset)
            : BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan().Slice(offset));

    private sealed class Repository(IEnumerable<TypeDefinitionModel> definitions) :
        ITypeDefinitionResolver, ITypeIdentityResolver
    {
        private readonly Dictionary<EntityKey, TypeDefinitionModel> _definitions =
            definitions.ToDictionary(definition => definition.Key);

        public CliTypeIdentity GetTypeIdentity(EntityKey key) =>
            CliTypeIdentity.FromDefinition(_definitions[key]);

        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) =>
            _definitions.Values.Single(definition =>
                GetTypeIdentity(definition.Key).Equals(identity.ElementType ?? identity));
    }
}
