// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Layout;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

public sealed class EnumMetadataCollectorTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void RepresentationDoesNotImplicitlyRequestMembers(WasmTarget target)
    {
        var state = new ManagedStaticDataBuildState();
        var collector = Create(target, [], state);

        Assert.Empty(state.PendingEnumMetadata);
        collector.Collect();

        Assert.Equal(2, state.PendingEnumMetadata.Count);
        Assert.All(state.PendingEnumMetadata.Values, metadata =>
        {
            Assert.Empty(metadata.Members);
            Assert.Equal(EnumMetadataPayload.None, metadata.Payload);
            Assert.Equal(Underlying, metadata.UnderlyingType);
            Assert.True(metadata.IsFlags);
        });
        Assert.Equal(1, state.PendingEnumMetadata[Identity(First)].TypeId);
        Assert.Equal(2, state.PendingEnumMetadata[Identity(Second)].TypeId);
        Assert.Empty(state.Strings);
        Assert.Empty(state.Segments);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ClosedRequestsUnionWithoutRetainingUnrelatedPayloads(WasmTarget target)
    {
        var state = new ManagedStaticDataBuildState();
        var collector = Create(target,
            [new(First, EnumMetadataPayload.Names), new(First, EnumMetadataPayload.Values)],
            state);

        collector.Collect();

        var first = state.PendingEnumMetadata[Identity(First)];
        Assert.Equal(EnumMetadataPayload.Names | EnumMetadataPayload.Values, first.Payload);
        Assert.Equal(Members, first.Members);
        Assert.Empty(state.PendingEnumMetadata[Identity(Second)].Members);
        Assert.Equal(EnumMetadataPayload.None, state.PendingEnumMetadata[Identity(Second)].Payload);
        Assert.DoesNotContain(Identity(Plain), state.PendingEnumMetadata.Keys);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ConservativeRequestsUnionWithClosedRequirements(WasmTarget target)
    {
        var state = new ManagedStaticDataBuildState();
        var collector = Create(target,
            [new(null, EnumMetadataPayload.Names), new(First, EnumMetadataPayload.Values)],
            state);

        collector.Collect();

        Assert.Equal(EnumMetadataPayload.Names | EnumMetadataPayload.Values,
            state.PendingEnumMetadata[Identity(First)].Payload);
        Assert.Equal(EnumMetadataPayload.Names, state.PendingEnumMetadata[Identity(Second)].Payload);
        Assert.All(state.PendingEnumMetadata.Values, metadata => Assert.Equal(Members, metadata.Members));
        Assert.Equal(2, state.PendingEnumMetadata.Count);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm64, false)]
    [InlineData(WasmTarget.Wasm32, true)]
    [InlineData(WasmTarget.Wasm64, true)]
    public void KeepsExactNestedEnumIdentitiesAndDefinitionPayloads(
        WasmTarget target, bool retainPayload)
    {
        var definitions = new NestedDefinitions();
        var named = definitions.GetTypeIdentity(NestedDefinitions.EnumKey);
        var intEnum = CliTypeIdentity.GenericInstantiation(named,
            [CliTypeIdentity.Primitive("i4", CliValueKind.I4)]);
        var stringEnum = CliTypeIdentity.GenericInstantiation(named,
            [CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference, false)]);
        var openEnum = CliTypeIdentity.GenericInstantiation(named,
            [CliTypeIdentity.GenericParameter(false, 0)]);
        var plain = CliTypeIdentity.GenericInstantiation(
            definitions.GetTypeIdentity(NestedDefinitions.PlainKey), [Underlying]);
        var namedLayout = new ObjectLayout(2, 0, []);
        var types = new ManagedTypeLayouts(
            new Dictionary<EntityKey, ObjectLayout>
            {
                [NestedDefinitions.EnumKey] = namedLayout,
            }.ToImmutableDictionary(),
            new Dictionary<CliTypeIdentity, ObjectLayout>
            {
                [named] = namedLayout,
            }.ToImmutableDictionary(), [],
            new Dictionary<CliTypeIdentity, ObjectLayout>
            {
                [named] = namedLayout,
                [openEnum] = new(12, 0, []),
                [intEnum] = new(7, 8, []),
                [stringEnum] = new(9, 8, []),
                [CliTypeIdentity.SzArray(intEnum)] = new(10, 8, []),
                [plain] = new(11, 8, []),
            }.ToImmutableDictionary(), new ManagedValueLayoutState(),
            WasmTargetLayout.For(target), -1, -1, -1, -1);
        var payload = retainPayload
            ? EnumMetadataPayload.Values | EnumMetadataPayload.Names
            : EnumMetadataPayload.None;
        var state = new ManagedStaticDataBuildState();
        var collector = As<IEnumMetadataCollector>(new EnumMetadataCollector(
            definitions, definitions, types, [new(NestedDefinitions.EnumKey, payload)], state));

        collector.Collect();

        Assert.Equal(4, state.PendingEnumMetadata.Count);
        Assert.Equal([2, 7, 9, 12], state.PendingEnumMetadata.Values.Select(item => item.TypeId));
        Assert.True(state.PendingEnumMetadata[named].IsOpenDefinition);
        Assert.True(state.PendingEnumMetadata[openEnum].IsOpenDefinition);
        Assert.False(state.PendingEnumMetadata[intEnum].IsOpenDefinition);
        Assert.False(state.PendingEnumMetadata[stringEnum].IsOpenDefinition);
        Assert.Same(intEnum, state.PendingEnumMetadata[intEnum].EnumType);
        Assert.Same(stringEnum, state.PendingEnumMetadata[stringEnum].EnumType);
        Assert.All(state.PendingEnumMetadata, pair =>
        {
            Assert.Equal(pair.Key, pair.Value.EnumType);
            Assert.Equal(NestedDefinitions.EnumKey, pair.Value.Type);
            Assert.Equal(Underlying, pair.Value.UnderlyingType);
            Assert.Equal(payload, pair.Value.Payload);
            Assert.Equal(retainPayload ? Members : [], pair.Value.Members);
        });
        Assert.Empty(state.Segments);
        Assert.Empty(state.ValueTypeDescriptors);
        Assert.Equal(0, types.Objects[NestedDefinitions.EnumKey].Size);
    }

    private static IEnumMetadataCollector Create(
        WasmTarget target,
        ImmutableHashSet<EnumMetadataRequirement> requirements,
        ManagedStaticDataBuildState state) =>
        As<IEnumMetadataCollector>(new EnumMetadataCollector(
            new Definitions(), new Definitions(), new ManagedTypeLayouts(
            new Dictionary<EntityKey, ObjectLayout>
            {
                [First] = new(1, 8, []),
                [Second] = new(2, 8, []),
                [Plain] = new(3, 8, []),
            }.ToImmutableDictionary(), [], [], [], new ManagedValueLayoutState(),
            WasmTargetLayout.For(target), -1, -1, -1, -1), requirements, state));

    private static TContract As<TContract>(TContract actor) => actor;

    private static readonly AssemblyIdentity Assembly = new("EnumDemandTests");
    private static readonly EntityKey First = new(Assembly, 1);
    private static readonly EntityKey Second = new(Assembly, 2);
    private static readonly EntityKey Plain = new(Assembly, 3);
    private static readonly CliTypeIdentity Underlying = CliTypeIdentity.Primitive("u4", CliValueKind.I4);
    private static readonly ImmutableArray<EnumMemberModel> Members = [new("One", 1), new("Zero", 0)];

    private static CliTypeIdentity Identity(EntityKey key) =>
        CliTypeIdentity.Named(Assembly, "Fixtures", "Value" + key.MetadataToken, true, CliValueKind.I4);

    private sealed class NestedDefinitions : ITypeDefinitionResolver, ITypeIdentityResolver
    {
        internal static readonly EntityKey EnumKey = new(Assembly, 4);
        internal static readonly EntityKey PlainKey = new(Assembly, 5);

        public CliTypeIdentity GetTypeIdentity(EntityKey key) =>
            CliTypeIdentity.FromDefinition(Definition(key));

        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity)
        {
            var named = identity.ElementType ?? identity;
            return Definition(new[] { EnumKey, PlainKey }
                .Single(key => named.Equals(GetTypeIdentity(key))));
        }

        private static TypeDefinitionModel Definition(EntityKey key) =>
            new(key, "Fixtures", key == EnumKey ? "Outer`1+Code" : "Outer`1", true, [], [])
            {
                GenericArity = 1,
                IsEnum = key == EnumKey,
                EnumUnderlyingType = Underlying,
                EnumMembers = Members,
            };
    }

    private sealed class Definitions : ITypeDefinitionResolver, ITypeIdentityResolver
    {
        public CliTypeIdentity GetTypeIdentity(EntityKey key) => Identity(key);

        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) =>
            GetTypeDefinition(new[] { First, Second, Plain }.Single(key => Identity(key).Equals(identity)));

        private static TypeDefinitionModel GetTypeDefinition(EntityKey key) =>
            new(key, "Fixtures", "Value" + key.MetadataToken, true, [], [])
            {
                IsEnum = key != Plain,
                IsFlagsEnum = true,
                EnumUnderlyingType = Underlying,
                EnumMembers = Members,
            };
    }
}
