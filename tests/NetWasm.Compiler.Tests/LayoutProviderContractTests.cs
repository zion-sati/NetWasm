using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Layout;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

public sealed class LayoutProviderContractTests
{
    [Fact]
    public void ProvidersReadTheImmutableSnapshotThroughTheirNarrowContracts()
    {
        var assembly = new AssemblyIdentity("LayoutProviderTests");
        var typeKey = new EntityKey(assembly, 1);
        var fieldKey = new EntityKey(assembly, 2);
        var type = CliTypeIdentity.Named(assembly, "Tests", "Node", false);
        var objectLayout = new ObjectLayout(7, 12, [4]);
        var valueLayout = new ValueLayout(type, 8, 4, [0]);
        var fieldLayout = new FieldLayout(4) { Size = 8, Type = type };
        var staticFieldLayout = new StaticFieldLayout(32);
        var stringLayout = new StringLayout(48, 5, 8);
        var dataSegment = new DataSegment(48, [1, 2, 3]);
        var state = new ManagedValueLayoutState();
        state.Fields.Add(fieldKey, fieldLayout);
        state.Values.Add(type, valueLayout);
        var types = new ManagedTypeLayouts(
            ImmutableDictionary<EntityKey, ObjectLayout>.Empty.Add(typeKey, objectLayout),
            ImmutableDictionary<CliTypeIdentity, ObjectLayout>.Empty.Add(type, objectLayout),
            ImmutableDictionary<string, ObjectLayout>.Empty.Add("Tests.Node", objectLayout),
            ImmutableDictionary<CliTypeIdentity, ObjectLayout>.Empty,
            state,
            WasmTargetLayout.For(WasmTarget.Wasm32),
            1,
            2,
            3,
            4);
        var staticData = new ManagedStaticData(
            64,
            ImmutableDictionary<EntityKey, StaticFieldLayout>.Empty.Add(
                fieldKey,
                staticFieldLayout),
            ImmutableDictionary<string, StaticFieldLayout>.Empty,
            ImmutableDictionary<string, StringLayout>.Empty.Add("hello", stringLayout),
            ImmutableDictionary<ManagedExceptionKind, int>.Empty.Add(
                ManagedExceptionKind.NullReference,
                56),
            [dataSegment],
            [],
            [],
            [],
            [32]);
        var snapshot = new ManagedLayoutSnapshot(types, staticData);

        var typeProvider = new TypeLayoutProviderFactory().Create(
            new RejectingResolver(),
            snapshot);
        var valueProvider = new ValueLayoutProviderFactory().Create(
            new FixedValueLayoutResolver(valueLayout));
        var instanceFields = new InstanceFieldLayoutProviderFactory().Create(snapshot);
        var staticFields = new StaticFieldLayoutProviderFactory().Create(snapshot);
        var staticDataProvider = new StaticDataLayoutProviderFactory().Create(snapshot);
        var memberDescriptors = new MemberDescriptorLayoutProviderFactory().Create(snapshot);
        var exceptionObjects = new ManagedExceptionObjectProviderFactory().Create(snapshot);

        Assert.Equal(objectLayout, typeProvider.GetObjectLayout(typeKey));
        Assert.Equal(objectLayout, typeProvider.GetObjectLayout(type));
        Assert.Equal(valueLayout, valueProvider.GetValueLayout(type));
        Assert.Equal(fieldLayout, instanceFields.GetFieldLayout(fieldKey));
        Assert.Equal(staticFieldLayout, staticFields.GetStaticFieldLayout(fieldKey));
        Assert.Equal(stringLayout, staticDataProvider.GetStringLayout("hello"));
        Assert.Equal(dataSegment, Assert.Single(staticDataProvider.DataSegments));
        Assert.Equal(32, Assert.Single(staticDataProvider.StaticRootAddresses));
        Assert.Equal(56, exceptionObjects.GetExceptionObject(
            ManagedExceptionKind.NullReference));
        Assert.Equal(64, staticDataProvider.StaticDataEnd);
        Assert.Equal(0, staticDataProvider.TypeTableAddress);
        Assert.IsType<MemberDescriptorLayoutProvider>(memberDescriptors);
        Assert.IsNotAssignableFrom<IMemberDescriptorLayout>(staticDataProvider);
    }

    private sealed class FixedValueLayoutResolver(ValueLayout layout) : IValueLayoutResolver
    {
        public ValueLayout Resolve(CliTypeIdentity type) => layout;

        public ValueLayout Resolve(CliTypeIdentity type, TypeDefinitionModel definition)
            => Resolve(type);
    }

    private sealed class RejectingResolver : ITypeDefinitionResolver
    {
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) =>
            throw new NotSupportedException(identity.CanonicalName);
    }
}
