using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class EnumStorageResolverWidthTests
{
    [Theory]
    [InlineData("i1", CliValueKind.I4, 1, 1, 4)]
    [InlineData("u1", CliValueKind.I4, 1, 1, 4)]
    [InlineData("i2", CliValueKind.I4, 2, 2, 4)]
    [InlineData("u2", CliValueKind.I4, 2, 2, 4)]
    [InlineData("i4", CliValueKind.I4, 4, 4, 4)]
    [InlineData("u4", CliValueKind.I4, 4, 4, 4)]
    [InlineData("i8", CliValueKind.I8, 8, 8, 8)]
    [InlineData("u8", CliValueKind.I8, 8, 8, 8)]
    public void ResolvesEveryEnumStorageWidth(
        string name,
        CliValueKind stackKind,
        int size,
        int alignment,
        int payloadOffset)
    {
        var assembly = new AssemblyIdentity("EnumStorageWidthTests");
        var enumType = new EntityKey(assembly, 1);
        var field = new EntityKey(assembly, 2);
        var underlying = CliTypeIdentity.Primitive(name, stackKind);
        var repository = new Repository(
            [new TypeDefinitionModel(
                enumType,
                "Tests",
                "State",
                true,
                [field],
                []) { IsEnum = true }],
            [new FieldDefinitionModel(field, enumType, "value__", underlying, false)],
            [new TypeDescriptorLayout(enumType, 7, 0, 16, 0, 0, null)],
            new ValueLayout(underlying, size, alignment, []));

        var result = Assert.Single(((IEnumStorageResolver)new EnumStorageResolver(
            repository,
            repository,
            repository,
            repository,
            repository)).Resolve());

        Assert.Equal(enumType, result.Descriptor.Type);
        Assert.Equal(
            CliTypeIdentity.Named(assembly, "Tests", "State", true),
            result.EnumType);
        Assert.Equal(underlying, result.UnderlyingType);
        Assert.Equal(size, result.Layout.Size);
        Assert.Equal(alignment, result.Layout.Alignment);
        Assert.Equal(payloadOffset, result.PayloadOffset);
    }

    [Fact]
    public void ReturnsNoStorageWhenNoDescriptorsAreReachable()
    {
        var repository = new Repository([], [], [], null);

        var result = ((IEnumStorageResolver)new EnumStorageResolver(
            repository,
            repository,
            repository,
            repository,
            repository)).Resolve();

        Assert.Empty(result);
    }

    [Fact]
    public void RejectsAnEnumWithoutValueField()
    {
        var assembly = new AssemblyIdentity("EnumStorageFailureTests");
        var enumType = new EntityKey(assembly, 1);
        var field = new EntityKey(assembly, 2);
        var repository = new Repository(
            [new TypeDefinitionModel(
                enumType,
                "Tests",
                "MissingValue",
                true,
                [field],
                []) { IsEnum = true }],
            [new FieldDefinitionModel(
                field,
                enumType,
                "not_value",
                CliTypeIdentity.Primitive("i4", CliValueKind.I4),
                false)],
            [new TypeDescriptorLayout(enumType, 7, 0, 16, 0, 0, null)],
            null);

        _ = Assert.Throws<InvalidOperationException>(() =>
            ((IEnumStorageResolver)new EnumStorageResolver(
                repository,
                repository,
                repository,
                repository,
                repository)).Resolve());
    }

    [Fact]
    public void RejectsDuplicateValueFields()
    {
        var assembly = new AssemblyIdentity("EnumStorageFailureTests");
        var enumType = new EntityKey(assembly, 1);
        var first = new EntityKey(assembly, 2);
        var second = new EntityKey(assembly, 3);
        var underlying = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var repository = new Repository(
            [new TypeDefinitionModel(
                enumType,
                "Tests",
                "DuplicateValue",
                true,
                [first, second],
                []) { IsEnum = true }],
            [
                new FieldDefinitionModel(first, enumType, "value__", underlying, false),
                new FieldDefinitionModel(second, enumType, "value__", underlying, false),
            ],
            [new TypeDescriptorLayout(enumType, 7, 0, 16, 0, 0, null)],
            new ValueLayout(underlying, 4, 4, []));

        _ = Assert.Throws<InvalidOperationException>(() =>
            ((IEnumStorageResolver)new EnumStorageResolver(
                repository,
                repository,
                repository,
                repository,
                repository)).Resolve());
    }

    [Fact]
    public void RejectsDescriptorWithUnknownType()
    {
        var assembly = new AssemblyIdentity("EnumStorageFailureTests");
        var missingType = new EntityKey(assembly, 99);
        var repository = new Repository(
            [],
            [],
            [new TypeDescriptorLayout(missingType, 7, 0, 16, 0, 0, null)],
            null);

        Assert.Throws<KeyNotFoundException>(() =>
            ((IEnumStorageResolver)new EnumStorageResolver(
                repository,
                repository,
                repository,
                repository,
                repository)).Resolve());
    }

    private sealed class Repository(
        IEnumerable<TypeDefinitionModel> definitions,
        IEnumerable<FieldDefinitionModel> fields,
        IEnumerable<TypeDescriptorLayout> descriptors,
        ValueLayout? valueLayout) :
        ITypeRepository,
        IFieldRepository,
        ITargetLayout,
        IValueLayoutProvider,
        ITypeDescriptorSource
    {
        private readonly Dictionary<EntityKey, TypeDefinitionModel> _definitions =
            definitions.ToDictionary(definition => definition.Key);
        private readonly Dictionary<EntityKey, FieldDefinitionModel> _fields =
            fields.ToDictionary(field => field.Key);

        public WasmTargetLayout Target => WasmTargetLayout.Wasm32;

        public ImmutableArray<TypeDescriptorLayout> TypeDescriptors =>
            [.. descriptors];

        public ImmutableArray<ConstructedTypeDescriptorLayout> ConstructedTypeDescriptors => [];

        public ImmutableArray<ValueTypeDescriptorLayout> ValueTypeDescriptors => [];

        public TypeDefinitionModel GetTypeDefinition(EntityKey key) => _definitions[key];

        public FieldDefinitionModel GetField(EntityKey key) => _fields[key];

        public ValueLayout GetValueLayout(CliTypeIdentity type) => valueLayout ??
            throw new InvalidOperationException($"no value layout for '{type}'");
    }
}
