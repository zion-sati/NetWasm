using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class EnumStorageResolverTests
{
    [Fact]
    public void ResolvesOnlyTypesMarkedAsEnums()
    {
        var assembly = new AssemblyIdentity("EnumResolverTests");
        var enumType = new EntityKey(assembly, 1);
        var plainType = new EntityKey(assembly, 2);
        var enumField = new EntityKey(assembly, 3);
        var repository = new Repository(
            [
                (enumType, new TypeDefinitionModel(
                    enumType,
                    "Tests",
                    "State",
                    true,
                    [enumField],
                    []) { IsEnum = true }),
                (plainType, new TypeDefinitionModel(
                    plainType,
                    "Tests",
                    "Plain",
                    true,
                    [],
                    [])),
            ],
            [
                (enumField, new FieldDefinitionModel(
                    enumField,
                    enumType,
                    "value__",
                    CliTypeIdentity.FromStackKind(CliValueKind.I4),
                    false)),
            ]);
        var layouts = new ResolverLayouts(
            [
                new(enumType, 7, 0, 16, 0, 0, null),
                new(plainType, 8, 0, 16, 0, 0, null),
            ]);
        var resolver = new EnumStorageResolver(
            repository,
            repository,
            layouts,
            layouts,
            layouts);

        var storage = ResolveThroughCapability(resolver);

        var result = Assert.Single(storage);
        Assert.Equal(enumType, result.Descriptor.Type);
        Assert.Equal(CliValueKind.I4, result.UnderlyingType.StackKind);
        Assert.Equal(4, result.Layout.Size);
        Assert.Equal(
            WasmTargetLayout.Align(layouts.Target.ObjectHeaderSize, result.Layout.Alignment),
            result.PayloadOffset);
    }

    private sealed class Repository(
        IEnumerable<(EntityKey Key, TypeDefinitionModel Definition)> types,
        IEnumerable<(EntityKey Key, FieldDefinitionModel Definition)> fields) :
        ITypeRepository,
        IFieldRepository
    {
        private readonly Dictionary<EntityKey, TypeDefinitionModel> _types =
            types.ToDictionary(entry => entry.Key, entry => entry.Definition);
        private readonly Dictionary<EntityKey, FieldDefinitionModel> _fields =
            fields.ToDictionary(entry => entry.Key, entry => entry.Definition);

        public TypeDefinitionModel GetTypeDefinition(EntityKey key) => _types[key];

        public FieldDefinitionModel GetField(EntityKey key) => _fields[key];
    }

    private static ImmutableArray<EnumStorage> ResolveThroughCapability(
        EnumStorageResolver resolver) => ((IEnumStorageResolver)resolver).Resolve();

    private sealed class ResolverLayouts(ImmutableArray<TypeDescriptorLayout> descriptors) :
        ITargetLayout,
        IValueLayoutProvider,
        ITypeDescriptorSource
    {
        public WasmTargetLayout Target => WasmTargetLayout.Wasm32;

        public ImmutableArray<TypeDescriptorLayout> TypeDescriptors => descriptors;

        public ImmutableArray<ConstructedTypeDescriptorLayout> ConstructedTypeDescriptors => [];

        public ImmutableArray<ValueTypeDescriptorLayout> ValueTypeDescriptors => [];

        public ValueLayout GetValueLayout(CliTypeIdentity type) => new(type, 4, 4, []);
    }
}
