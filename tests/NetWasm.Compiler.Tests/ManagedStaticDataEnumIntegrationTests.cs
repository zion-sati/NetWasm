using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Layout;

namespace NetWasm.Compiler.Tests;

public sealed class ManagedStaticDataEnumIntegrationTests
{
    [Fact]
    public void BuildInvokesEnumMetadataAndPublishesItThroughSnapshot()
    {
        var assembly = new AssemblyIdentity("ManagedStaticDataEnumTests");
        var enumType = new EntityKey(assembly, 1);
        var metadata = new EnumMetadataLayout(
            enumType,
            CliTypeIdentity.Named(assembly, "Tests", "Flags", true, CliValueKind.I4),
            7,
            64,
            CliTypeIdentity.Primitive("u2", CliValueKind.I4),
            true,
            false,
            [new EnumMetadataMemberLayout(
                "Read",
                1,
                new StringLayout(100, 4, 8))]);
        var state = new ManagedStaticDataBuildState();
        var enumBuilder = new RecordingEnumMetadataBuilder(state, metadata);
        var phases = new NoOpStaticBuildPhase();
        var types = EmptyLayouts();

        var result = new ManagedStaticDataBuilder(
            types,
            state,
            phases,
            phases,
            phases,
            phases,
            phases,
            phases,
            phases,
            phases,
            phases,
            phases,
            phases,
            enumBuilder).Build();

        Assert.True(enumBuilder.Called);
        Assert.Equal(metadata, Assert.Single(result.EnumMetadata));
        var snapshot = new ManagedLayoutSnapshot(types, result);
        Assert.Equal(metadata, Assert.Single(snapshot.EnumMetadata));
    }

    [Fact]
    public void BuildPublishesEmptyEnumMetadataWhenCollectorFindsNothing()
    {
        var state = new ManagedStaticDataBuildState();
        var phases = new NoOpStaticBuildPhase();

        var result = new ManagedStaticDataBuilder(
            EmptyLayouts(),
            state,
            phases,
            phases,
            phases,
            phases,
            phases,
            phases,
            phases,
            phases,
            phases,
            phases,
            phases,
            phases).Build();

        Assert.Empty(result.EnumMetadata);
        Assert.Empty(state.EnumMetadata);
    }

    [Fact]
    public void ManagedStaticDataDefaultsToNoEnumMetadata()
    {
        var data = new ManagedStaticData(16, [], [], [], [], [], [], [], [], []);

        Assert.Empty(data.EnumMetadata);
    }

    // The type table describes every registered type, member descriptors included, so it is
    // built after them; its address is what the runtime initializer and CoreLib read.
    [Fact]
    public void BuildRunsTheTypeTableAfterMemberDescriptorsAndPublishesItsAddress()
    {
        var state = new ManagedStaticDataBuildState();
        var phases = new NoOpStaticBuildPhase();
        var members = new RecordingMemberDescriptorBuilder();
        var table = new RecordingTypeTableBuilder(state, members);

        var result = new ManagedStaticDataBuilder(
            EmptyLayouts(),
            state,
            phases,
            phases,
            phases,
            phases,
            phases,
            table,
            members,
            phases,
            phases,
            phases,
            phases,
            phases).Build();

        Assert.True(table.SawMemberDescriptors);
        Assert.Equal(777, result.TypeTableAddress);
    }

    [Fact]
    public void BuilderRejectsNullRequiredDependencies()
    {
        var types = EmptyLayouts();
        var state = new ManagedStaticDataBuildState();
        var phases = new NoOpStaticBuildPhase();
        object?[] arguments = [types, state, .. Enumerable.Repeat<object>(phases, 12)];
        string[] names =
        [
            "types", "state", "staticFields", "typeDescriptors", "constructedTypeDescriptors",
            "valueTypeDescriptors", "genericArguments", "typeTable", "memberDescriptors", "strings",
            "exceptionObjects", "typeObjects", "enumMetadataCollector", "enumMetadata",
        ];

        for (var index = 0; index < arguments.Length; index++)
        {
            var withNull = (object?[])arguments.Clone();
            withNull[index] = null;
            Assert.Equal(names[index], Assert.Throws<ArgumentNullException>(() => Create(withNull)).ParamName);
        }
        Assert.Throws<ArgumentNullException>(() => Create([types with { Target = null! }, .. arguments[1..]]));
    }

    private static ManagedStaticDataBuilder Create(object?[] arguments) => new(
        (ManagedTypeLayouts)arguments[0]!,
        (ManagedStaticDataBuildState)arguments[1]!,
        (IStaticFieldStorageBuilder)arguments[2]!,
        (ITypeDescriptorBuilder)arguments[3]!,
        (IConstructedTypeDescriptorBuilder)arguments[4]!,
        (IValueTypeDescriptorBuilder)arguments[5]!,
        (IRuntimeGenericArgumentMetadataBuilder)arguments[6]!,
        (IRuntimeTypeTableBuilder)arguments[7]!,
        (IMemberDescriptorDataBuilder)arguments[8]!,
        (IStringDataBuilder)arguments[9]!,
        (IExceptionObjectBuilder)arguments[10]!,
        (ITypeObjectDataBuilder)arguments[11]!,
        (IEnumMetadataCollector)arguments[12]!,
        (IEnumMetadataBuilder)arguments[13]!);

    private static ManagedTypeLayouts EmptyLayouts() =>
        new(
            ImmutableDictionary<EntityKey, ObjectLayout>.Empty,
            ImmutableDictionary<CliTypeIdentity, ObjectLayout>.Empty,
            ImmutableDictionary<string, ObjectLayout>.Empty,
            ImmutableDictionary<CliTypeIdentity, ObjectLayout>.Empty,
            new ManagedValueLayoutState(),
            WasmTargetLayout.Wasm32,
            -1,
            -1,
            -1,
            -1);

    private sealed class RecordingEnumMetadataBuilder(
        ManagedStaticDataBuildState state,
        EnumMetadataLayout metadata) : IEnumMetadataBuilder
    {
        public bool Called { get; private set; }

        public void Build()
        {
            Called = true;
            state.EnumMetadata.Add(metadata);
        }
    }

    private sealed class RecordingMemberDescriptorBuilder : IMemberDescriptorDataBuilder
    {
        public bool Built { get; private set; }

        public void Build() => Built = true;
    }

    private sealed class RecordingTypeTableBuilder(
        ManagedStaticDataBuildState state,
        RecordingMemberDescriptorBuilder members) : IRuntimeTypeTableBuilder
    {
        public bool SawMemberDescriptors { get; private set; }

        public void Build()
        {
            SawMemberDescriptors = members.Built;
            state.TypeTableAddress = 777;
        }
    }

    private sealed class NoOpStaticBuildPhase :
        IStaticFieldStorageBuilder,
        ITypeDescriptorBuilder,
        IConstructedTypeDescriptorBuilder,
        IValueTypeDescriptorBuilder,
        IRuntimeGenericArgumentMetadataBuilder,
        IRuntimeTypeTableBuilder,
        IMemberDescriptorDataBuilder,
        IStringDataBuilder,
        IExceptionObjectBuilder,
        ITypeObjectDataBuilder,
        IEnumMetadataCollector,
        IEnumMetadataBuilder
    {
        public void Build()
        {
        }

        public void Collect()
        {
        }
    }
}
