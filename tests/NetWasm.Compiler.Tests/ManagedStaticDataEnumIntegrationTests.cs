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

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void BuildPublishesSparseTypeFactsLookupTable(WasmTarget targetKind)
    {
        var target = WasmTargetLayout.For(targetKind);
        var state = new ManagedStaticDataBuildState();
        state.TypeFacts.Add(2, 64);
        state.TypeFacts.Add(7, 128);
        var assembly = new AssemblyIdentity("type-facts");
        state.TypeDescriptors.Add(new TypeDescriptorLayout(
            new EntityKey(assembly, 1),
            2,
            0,
            8,
            0,
            0,
            null));
        state.TypeDescriptors.Add(new TypeDescriptorLayout(
            new EntityKey(assembly, 2),
            7,
            0,
            8,
            0,
            0,
            null));
        var phases = new NoOpStaticBuildPhase();
        var layouts = EmptyLayouts() with { Target = target };

        var result = new ManagedStaticDataBuilder(
            layouts,
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
            phases).Build();

        Assert.Equal(8, result.TypeFactsTableCount);
        Assert.NotEqual(0, result.TypeFactsTableAddress);
        var table = Assert.Single(
            result.Segments,
            segment => segment.Address == result.TypeFactsTableAddress).Data;
        Assert.Equal(8 * target.AddressSize, table.Length);
        Assert.Equal(64, ReadAddress(table, 2 * target.AddressSize, target));
        Assert.Equal(128, ReadAddress(table, 7 * target.AddressSize, target));
        Assert.Equal(0, ReadAddress(table, 3 * target.AddressSize, target));
    }

    [Fact]
    public void BuildRejectsTypeFactsThatDoNotMatchReachableDescriptors()
    {
        var state = new ManagedStaticDataBuildState();
        state.TypeFacts.Add(2, 64);
        state.TypeDescriptors.Add(new TypeDescriptorLayout(
            new EntityKey(new AssemblyIdentity("type-facts"), 1),
            3,
            0,
            8,
            0,
            0,
            null));
        var phases = new NoOpStaticBuildPhase();

        var exception = Assert.Throws<CompilerException>(() =>
            new ManagedStaticDataBuilder(
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
                phases).Build());

        Assert.Equal(DiagnosticCode.RuntimeContract, exception.Diagnostic.Code);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void BuildPublishesDelegateInvokeAfterMemberDescriptorStorage(
        WasmTarget targetKind)
    {
        const string descriptor = "Fixtures.Callback::Invoke";
        const int descriptorAddress = 512;
        var target = WasmTargetLayout.For(targetKind);
        var state = PendingDelegateFacts(target, descriptor);
        var phases = new NoOpStaticBuildPhase();
        var members = new RecordingMemberDescriptorBuilder(
            state,
            descriptor,
            descriptorAddress);

        var result = new ManagedStaticDataBuilder(
            EmptyLayouts() with { Target = target },
            state,
            phases,
            phases,
            phases,
            phases,
            phases,
            members,
            phases,
            phases,
            phases,
            phases,
            phases).Build();

        Assert.False(members.SawPublishedTypeFacts);
        var factsAddress = state.TypeFacts[2];
        var facts = Assert.Single(
            result.Segments,
            segment => segment.Address == factsAddress).Data;
        Assert.Equal(
            descriptorAddress,
            ReadAddress(
                facts,
                RuntimeTypeFactsEncoding.DelegateInvokeOffset(target),
                target));
    }

    [Fact]
    public void BuildRejectsDelegateInvokeWithoutDescriptorStorage()
    {
        var target = WasmTargetLayout.Wasm32;
        var state = PendingDelegateFacts(target, "Fixtures.Callback::Invoke");
        var phases = new NoOpStaticBuildPhase();

        var exception = Assert.Throws<CompilerException>(() =>
            new ManagedStaticDataBuilder(
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
                phases).Build());

        Assert.Equal(DiagnosticCode.RuntimeContract, exception.Diagnostic.Code);
        Assert.Empty(state.TypeFacts);
    }

    [Fact]
    public void BuildPublishesZeroDelegateInvokeWhenNotDemanded()
    {
        var target = WasmTargetLayout.Wasm32;
        var state = PendingDelegateFacts(target, descriptor: null);
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
            phases).Build();

        var factsAddress = state.TypeFacts[2];
        var facts = Assert.Single(
            result.Segments,
            segment => segment.Address == factsAddress).Data;
        Assert.Equal(
            0,
            ReadAddress(
                facts,
                RuntimeTypeFactsEncoding.DelegateInvokeOffset(target),
                target));
    }

    [Fact]
    public void BuildPublishesDemandedRuntimeTypeNames()
    {
        var target = WasmTargetLayout.Wasm32;
        var state = PendingDelegateFacts(target, descriptor: null);
        state.Strings.Add("Callback", new StringLayout(512, 8, 8));
        state.PendingTypeFacts[0] = state.PendingTypeFacts[0] with
        {
            Names = new RuntimeTypeNames(
                RuntimeTypeNamePayload.Name,
                "Callback",
                null,
                null,
                null),
        };
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
            phases).Build();

        var factsAddress = state.TypeFacts[2];
        var facts = Assert.Single(
            result.Segments,
            segment => segment.Address == factsAddress).Data;
        var namesAddress = ReadAddress(
            facts,
            RuntimeTypeFactsEncoding.NameFactsOffset(target),
            target);
        Assert.NotEqual(0, namesAddress);
        Assert.Contains(result.Segments, segment => segment.Address == namesAddress);
    }

    [Fact]
    public void BuilderRejectsNullRequiredDependencies()
    {
        var types = EmptyLayouts();
        var state = new ManagedStaticDataBuildState();
        var phases = new NoOpStaticBuildPhase();

        Assert.Throws<ArgumentNullException>(() => new ManagedStaticDataBuilder(
            null!, state, phases, phases, phases, phases, phases, phases, phases, phases, phases, phases,
            phases));
        Assert.Throws<ArgumentNullException>(() => new ManagedStaticDataBuilder(
            types, null!, phases, phases, phases, phases, phases, phases, phases, phases, phases, phases,
            phases));
        Assert.Throws<ArgumentNullException>(() => new ManagedStaticDataBuilder(
            types, state, null!, phases, phases, phases, phases, phases, phases, phases, phases, phases,
            phases));
        Assert.Throws<ArgumentNullException>(() => new ManagedStaticDataBuilder(
            types, state, phases, null!, phases, phases, phases, phases, phases, phases, phases, phases,
            phases));
        Assert.Throws<ArgumentNullException>(() => new ManagedStaticDataBuilder(
            types, state, phases, phases, null!, phases, phases, phases, phases, phases, phases, phases,
            phases));
        Assert.Throws<ArgumentNullException>(() => new ManagedStaticDataBuilder(
            types, state, phases, phases, phases, null!, phases, phases, phases, phases, phases, phases,
            phases));
        Assert.Throws<ArgumentNullException>(() => new ManagedStaticDataBuilder(
            types, state, phases, phases, phases, phases, phases, null!, phases, phases, phases, phases,
            phases));
        var genericArguments = Assert.Throws<ArgumentNullException>(() =>
            new ManagedStaticDataBuilder(
                types,
                state,
                phases,
                phases,
                phases,
                phases,
                null!,
                phases,
                phases,
                phases,
                phases,
                phases,
                phases));
        Assert.Equal("genericArguments", genericArguments.ParamName);
        Assert.Throws<ArgumentNullException>(() => new ManagedStaticDataBuilder(
            types, state, phases, phases, phases, phases, phases, phases, null!, phases, phases, phases,
            phases));
        Assert.Throws<ArgumentNullException>(() => new ManagedStaticDataBuilder(
            types, state, phases, phases, phases, phases, phases, phases, phases, null!, phases, phases,
            phases));
        Assert.Throws<ArgumentNullException>(() => new ManagedStaticDataBuilder(
            types, state, phases, phases, phases, phases, phases, phases, phases, phases, phases, null!,
            phases));
        Assert.Throws<ArgumentNullException>(() => new ManagedStaticDataBuilder(
            types, state, phases, phases, phases, phases, phases, phases, phases, phases, phases, phases,
            null!));
        Assert.Throws<ArgumentNullException>(() => new ManagedStaticDataBuilder(
            types with { Target = null! },
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
            phases));
    }

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

    private static ManagedStaticDataBuildState PendingDelegateFacts(
        WasmTargetLayout target,
        string? descriptor)
    {
        var assembly = new AssemblyIdentity("delegate-facts");
        var key = new EntityKey(assembly, 1);
        var identity = CliTypeIdentity.Named(
            assembly,
            "Fixtures",
            "Callback",
            isValueType: false);
        var definition = new TypeDefinitionModel(
            key,
            "Fixtures",
            "Callback",
            IsValueType: false,
            [],
            []);
        var state = new ManagedStaticDataBuildState();
        state.TypeDescriptors.Add(new TypeDescriptorLayout(
            key,
            2,
            0,
            target.ObjectHeaderSize,
            0,
            0,
            null));
        state.PendingTypeFacts.Add(new PendingRuntimeTypeFacts(
            identity,
            definition,
            2,
            0,
            0,
            0,
            descriptor,
            null));
        return state;
    }

    private static long ReadAddress(
        ImmutableArray<byte> data,
        int offset,
        WasmTargetLayout target) => target.AddressSize == sizeof(int)
        ? System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(
            data.AsSpan().Slice(offset))
        : System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(
            data.AsSpan().Slice(offset));

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

    private sealed class RecordingMemberDescriptorBuilder(
        ManagedStaticDataBuildState state,
        string descriptor,
        int descriptorAddress) : IMemberDescriptorDataBuilder
    {
        public bool SawPublishedTypeFacts { get; private set; }

        public void Build()
        {
            SawPublishedTypeFacts = state.TypeFacts.Count != 0;
            state.MethodDescriptors.Add(descriptor, descriptorAddress);
        }
    }

    private sealed class NoOpStaticBuildPhase :
        IStaticFieldStorageBuilder,
        ITypeDescriptorBuilder,
        IConstructedTypeDescriptorBuilder,
        IValueTypeDescriptorBuilder,
        IRuntimeGenericArgumentMetadataBuilder,
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
