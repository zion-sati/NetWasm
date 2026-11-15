// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class CustomAttributeMemberResolverTests
{
    private static readonly AssemblyIdentity Assembly = new("AttributeMembers");
    private static readonly EntityKey TypeKey = new(Assembly, 0x02000001);
    private static readonly CliTypeIdentity Integer = CliTypeIdentity.FromStackKind(CliValueKind.I4);
    private static readonly CliTypeIdentity Target = CliTypeIdentity.GenericInstantiation(
        CliTypeIdentity.Named(Assembly, "Test", "Marker`1", false), [Integer]);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WritablePublicMembersAreResolvedWithTheClosedDeclaringContext(bool field)
    {
        using var fixture = new Fixture("valid");
        var binding = fixture.Resolve(field ? CustomAttributeNamedArgumentKind.Field : CustomAttributeNamedArgumentKind.Property);
        Assert.NotNull(binding);
        if (field)
        {
            Assert.Same(fixture.FieldInstance, binding.Field);
            Assert.Null(binding.Setter);
        }
        else
        {
            Assert.Null(binding.Field);
            Assert.Same(fixture.SetterInstance, binding.Setter);
        }
        Assert.Equal(1, fixture.InstanceReads);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("private")]
    [InlineData("different-name")]
    public void AbsentOrInaccessibleFieldsAllowBaseMemberLookup(string shape)
    {
        using var fixture = new Fixture(shape);
        Assert.Null(fixture.Resolve(CustomAttributeNamedArgumentKind.Field));
        Assert.Equal(0, fixture.InstanceReads);
    }

    [Theory]
    [InlineData("static")]
    [InlineData("literal")]
    [InlineData("readonly")]
    public void AccessibleNonWritableFieldsFailBeforeBinding(string shape)
    {
        using var fixture = new Fixture(shape);
        AssertUnsupported(fixture, CustomAttributeNamedArgumentKind.Field);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("private")]
    [InlineData("different-name")]
    [InlineData("different-owner")]
    [InlineData("no-accessors")]
    [InlineData("private-getter-only")]
    [InlineData("private-setter-only")]
    public void AbsentOrInaccessiblePropertiesAllowBaseMemberLookup(string shape)
    {
        using var fixture = new Fixture(shape);
        Assert.Null(fixture.Resolve(CustomAttributeNamedArgumentKind.Property));
        Assert.Equal(0, fixture.InstanceReads);
    }

    [Theory]
    [InlineData("public-setter-only")]
    [InlineData("private-getter-public-setter")]
    public void ASetterCanMakeAPropertyPublicWithoutAPublicGetter(string shape)
    {
        using var fixture = new Fixture(shape);
        var binding = fixture.Resolve(CustomAttributeNamedArgumentKind.Property);
        Assert.NotNull(binding);
        Assert.Same(fixture.SetterInstance, binding.Setter);
        Assert.Equal(1, fixture.InstanceReads);
    }

    [Theory]
    [InlineData("getter-only")]
    [InlineData("public-getter-private-setter")]
    [InlineData("static")]
    [InlineData("indexer")]
    [InlineData("zero-parameters")]
    [InlineData("nonvoid")]
    public void PublicPropertiesRequireAnOrdinaryWritableInstanceSetter(string shape)
    {
        using var fixture = new Fixture(shape);
        AssertUnsupported(fixture, CustomAttributeNamedArgumentKind.Property);
    }

    [Fact]
    public void UnknownMemberKindIsRejected()
    {
        using var fixture = new Fixture("valid");
        AssertUnsupported(fixture, (CustomAttributeNamedArgumentKind)0);
    }

    private static void AssertUnsupported(Fixture fixture, CustomAttributeNamedArgumentKind kind)
    {
        var exception = Assert.Throws<CompilerException>(() => fixture.Resolve(kind));
        Assert.Equal(DiagnosticCode.UnsupportedMetadata, exception.Diagnostic.Code);
        Assert.Contains("Value", exception.Diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(0, fixture.InstanceReads);
    }

    private sealed class Fixture : IDisposable, ITypeDefinitionResolver, IFieldRepository,
        IMethodRepository, IMetadataFieldReferenceResolver, IMethodInstanceResolver
    {
        private readonly MetadataReaderProvider _owner;
        private readonly MetadataAssemblySnapshot _source;
        private readonly TypeDefinitionModel _definition;
        private readonly FieldDefinitionModel _field;
        private readonly MethodDefinitionModel _getter;
        private readonly MethodDefinitionModel _setter;
        private readonly ImmutableArray<PropertyDefinitionModel> _properties;

        internal Fixture(string shape)
        {
            var metadata = new MetadataBuilder();
            metadata.AddModule(0, metadata.GetOrAddString("Members"), default, default, default);
            var handle = metadata.AddFieldDefinition(shape == "private" ? FieldAttributes.Private : FieldAttributes.Public,
                metadata.GetOrAddString("Value"), default);
            var image = new BlobBuilder();
            new MetadataRootBuilder(metadata).Serialize(image, 0, 0);
            _owner = MetadataReaderProvider.FromMetadataImage(image.ToImmutableArray());
            _source = new(Assembly, _owner.GetMetadataReader(),
                new Dictionary<int, TypeDefinitionModel>(), new Dictionary<int, FieldDefinitionModel>(),
                new Dictionary<int, MethodDefinitionModel>(), new Dictionary<int, EntityHandle>());
            _field = new(new(Assembly, MetadataTokens.GetToken(handle)), TypeKey,
                shape == "different-name" ? "Other" : "Value", Integer, shape == "static")
            { IsInitOnly = shape == "readonly", IsLiteral = shape == "literal" };
            _definition = new(TypeKey, "Test", "Marker`1", false,
                shape == "missing" ? [] : [_field.Key], [])
            { GenericArity = 1 };
            _getter = new(new(Assembly, 0x06000001), TypeKey, "get_Value", false,
                MethodSignatureModel.Create(CliValueKind.I4), 1)
            { IsPublic = shape is not ("private" or "private-getter-only" or "private-getter-public-setter") };
            _setter = new(new(Assembly, 0x06000002), TypeKey, "set_Value", shape == "static",
                new MethodSignatureModel(shape == "nonvoid" ? Integer : CliTypeIdentity.FromStackKind(CliValueKind.Void),
                    shape == "zero-parameters" ? [] : [Integer]), 1)
            { IsPublic = shape is not ("private" or "private-setter-only" or "public-getter-private-setter") };
            var property = new PropertyDefinitionModel(new(Assembly, 0x17000001),
                shape == "different-owner" ? new(Assembly, 0x02000002) : TypeKey,
                shape == "different-name" ? "Other" : "Value", Integer,
                shape == "indexer" ? [Integer] : [],
                shape is "no-accessors" or "public-setter-only" or "private-setter-only" ? null : _getter.Key,
                shape is "no-accessors" or "getter-only" or "private-getter-only" ? null : _setter.Key);
            _properties = shape == "missing" ? [] : [property];
            FieldInstance = new(_field, Target, Integer);
            SetterInstance = new(_setter, Target, [], _setter.Signature);
        }

        internal FieldInstanceModel FieldInstance { get; }
        internal MethodInstanceModel SetterInstance { get; }
        internal int InstanceReads { get; private set; }

        internal CustomAttributeMemberBinding? Resolve(CustomAttributeNamedArgumentKind kind) =>
            ((ICustomAttributeMemberResolver)new CustomAttributeMemberResolver(
                ImmutableDictionary<AssemblyIdentity, MetadataAssemblySnapshot>.Empty.Add(Assembly, _source),
                _properties, this, this, this, this, this)).Resolve(Target, "Value", kind);

        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity)
        {
            Assert.Same(Target, identity);
            return _definition;
        }

        public FieldDefinitionModel GetField(EntityKey key)
        {
            Assert.Equal(_field.Key, key);
            return _field;
        }

        public MethodDefinitionModel GetMethod(EntityKey key)
        {
            Assert.True(key == _getter.Key || key == _setter.Key);
            return key == _getter.Key ? _getter : _setter;
        }

        public FieldInstanceModel Resolve(MetadataAssemblySnapshot source, int metadataToken,
            string methodDisplayName, int ilOffset, CliGenericContext? genericContext = null)
        {
            Assert.Same(_source, source);
            Assert.Equal(_field.Key.MetadataToken, metadataToken);
            CheckBinding(methodDisplayName, ilOffset, genericContext);
            return FieldInstance;
        }

        public MethodInstanceModel ResolveMethodInstance(AssemblyIdentity source, int metadataToken,
            string methodDisplayName, int ilOffset, CliGenericContext? genericContext = null)
        {
            Assert.Equal(Assembly, source);
            Assert.Equal(_setter.Key.MetadataToken, metadataToken);
            CheckBinding(methodDisplayName, ilOffset, genericContext);
            return SetterInstance;
        }

        private void CheckBinding(string name, int offset, CliGenericContext? context)
        {
            Assert.Equal("Value", name);
            Assert.Equal(0, offset);
            Assert.Equal(Target.TypeArguments, context!.Value.TypeArguments);
            Assert.Empty(context.Value.MethodArguments);
            InstanceReads++;
        }

        public void Dispose() => _owner.Dispose();
    }
}
