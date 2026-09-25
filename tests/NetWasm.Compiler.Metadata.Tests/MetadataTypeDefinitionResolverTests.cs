using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class MetadataTypeDefinitionResolverTests
{
    [Theory]
    [InlineData("void", "Void")]
    [InlineData("bool", "Boolean")]
    [InlineData("char", "Char")]
    [InlineData("i1", "SByte")]
    [InlineData("u1", "Byte")]
    [InlineData("i2", "Int16")]
    [InlineData("u2", "UInt16")]
    [InlineData("i4", "Int32")]
    [InlineData("u4", "UInt32")]
    [InlineData("i8", "Int64")]
    [InlineData("u8", "UInt64")]
    [InlineData("f4", "Single")]
    [InlineData("f8", "Double")]
    [InlineData("nativeint", "IntPtr")]
    [InlineData("nativeuint", "UIntPtr")]
    [InlineData("string", "String")]
    [InlineData("object", "Object")]
    public void ResolveTypeIdentityMapsEverySupportedPrimitive(
        string primitiveName,
        string typeName)
    {
        var type = Type(new(new("Core"), 1), "System", typeName);
        var availability = MetadataActorTestData.CreateAvailabilityValidator();
        var resolver = new MetadataTypeDefinitionResolver(
            [type],
            ImmutableDictionary<string, string>.Empty,
            new MetadataTypeFinder([type], availability),
            availability);

        var result = ((ITypeDefinitionResolver)resolver).ResolveTypeIdentity(
            CliTypeIdentity.Primitive(primitiveName, CliValueKind.I4));

        Assert.Same(type, result);
    }

    [Fact]
    public void ResolveTypeIdentityHandlesPrimitiveNamedConstructedAndAliasIdentities()
    {
        var intType = Type(new(new("Core"), 1), "System", "Int32", isValueType: true);
        var genericType = Type(new(new("Implementation"), 2), "Test", "Box`1");
        var types = ImmutableArray.Create(intType, genericType);
        var availability = MetadataActorTestData.CreateAvailabilityValidator();
        var finder = new MetadataTypeFinder(types, availability);
        var resolver = new MetadataTypeDefinitionResolver(
            types,
            ImmutableDictionary<string, string>.Empty.Add("Facade", "Implementation"),
            finder,
            availability);
        var genericIdentity = CliTypeIdentity.Named(
            new("Facade"), "Test", "Box`1", isValueType: false);

        Assert.Same(
            intType,
            ((ITypeDefinitionResolver)resolver).ResolveTypeIdentity(
                CliTypeIdentity.Primitive("i4", CliValueKind.I4)));
        Assert.Same(
            genericType,
            ((ITypeDefinitionResolver)resolver).ResolveTypeIdentity(genericIdentity));
        Assert.Same(
            genericType,
            ((ITypeDefinitionResolver)resolver).ResolveTypeIdentity(
                CliTypeIdentity.GenericInstantiation(
                    genericIdentity,
                    [CliTypeIdentity.Primitive("i4", CliValueKind.I4)])));
    }

    [Fact]
    public void ResolveTypeIdentityRejectsNullUnsupportedPrimitiveMalformedAndMissingIdentities()
    {
        var availability = MetadataActorTestData.CreateAvailabilityValidator();
        var resolver = new MetadataTypeDefinitionResolver(
            [],
            ImmutableDictionary<string, string>.Empty,
            new MetadataTypeFinder([], availability),
            availability);
        Assert.Throws<ArgumentNullException>(() =>
            ((ITypeDefinitionResolver)resolver).ResolveTypeIdentity(null!));
        Assert.Throws<CompilerException>(() =>
            ((ITypeDefinitionResolver)resolver).ResolveTypeIdentity(
                CliTypeIdentity.Primitive("typedref", CliValueKind.Unknown)));
        Assert.Throws<CompilerException>(() =>
            ((ITypeDefinitionResolver)resolver).ResolveTypeIdentity(
                CliTypeIdentity.SzArray(
                    CliTypeIdentity.Primitive("i4", CliValueKind.I4))));
        Assert.Throws<CompilerException>(() =>
            ((ITypeDefinitionResolver)resolver).ResolveTypeIdentity(
                CliTypeIdentity.Named(new("Missing"), "Test", "Type", false)));
    }

    private static TypeDefinitionModel Type(
        EntityKey key,
        string @namespace,
        string name,
        bool isValueType = false) => new(key, @namespace, name, isValueType, [], []);
}
