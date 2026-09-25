using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class SignatureTypeProviderTests
{
    [Fact]
    public void ProvidesStandardSignatureShapesThroughItsPublicContract()
    {
        var provider = new SignatureTypeProvider(new("System.Runtime"));
        var element = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var signature = provider.GetArrayType(
            element,
            new ArrayShape(1, ImmutableArray<int>.Empty, ImmutableArray<int>.Empty));

        Assert.Equal(CliTypeShape.Array, signature.Shape);
        Assert.Equal(CliTypeShape.UnmanagedPointer, provider.GetFunctionPointerType(default).Shape);
        Assert.Same(element, provider.GetPinnedType(element));
        Assert.Equal(CliTypeShape.UnmanagedPointer, provider.GetPointerType(element).Shape);
        Assert.Equal(CliTypeShape.ManagedByReference, provider.GetByReferenceType(element).Shape);
        Assert.Equal(CliTypeShape.SzArray, provider.GetSZArrayType(element).Shape);
        Assert.Same(element, provider.GetModifiedType(
            CliTypeIdentity.Primitive("i4", CliValueKind.I4), element, isRequired: true));

        var fallback = provider.GetGenericTypeParameter(null, 0);
        Assert.Equal(CliTypeShape.GenericTypeParameter, fallback.Shape);
        Assert.Same(
            element,
            provider.GetGenericTypeParameter(
                new CliGenericContext([element], []),
                0));
    }

    [Fact]
    public void RetainsTypedReferenceIdentityForUnreachableDesktopSignatures()
    {
        var provider = new SignatureTypeProvider(new("System.Runtime"));

        var identity = provider.GetPrimitiveType(PrimitiveTypeCode.TypedReference);

        Assert.Equal(CliTypeShape.Primitive, identity.Shape);
        Assert.Equal("primitive:typedref", identity.CanonicalName);
        Assert.Equal(CliValueKind.ValueType, identity.StackKind);
    }

    [Fact]
    public void DecodesTypeSpecificationsThroughItsPublicContract()
    {
        using var assets = TestAssets.Create();
        using var assembly = ManagedAssemblyTestFactory.Load(assets.CoreLib);
        Assert.True(assembly.Reader.GetTableRowCount(TableIndex.TypeSpec) > 0);
        var handle = MetadataTokens.TypeSpecificationHandle(1);
        var provider = new SignatureTypeProvider(assembly.Identity, assembly.Reader);

        var identity = provider.GetTypeFromSpecification(
            assembly.Reader,
            new CliGenericContext([], []),
            handle,
            0x12);

        Assert.NotNull(identity);
    }

    [Fact]
    public void RejectsUnknownNamedSignatureKindThroughItsPublicContract()
    {
        using var assets = TestAssets.Create();
        using var assembly = ManagedAssemblyTestFactory.Load(assets.CoreLib);
        var provider = new SignatureTypeProvider(assembly.Identity, assembly.Reader);

        Assert.Throws<CompilerException>(() => provider.GetTypeFromDefinition(
            assembly.Reader,
            MetadataTokens.TypeDefinitionHandle(1),
            0xff));
    }

    [Fact]
    public void HandlesEveryRuntimePrimitiveCodeAndRejectsUnknownCodes()
    {
        var provider = new SignatureTypeProvider(new("System.Runtime"));

        foreach (var code in Enum.GetValues<PrimitiveTypeCode>())
        {
            _ = provider.GetPrimitiveType(code);
        }

        Assert.Throws<CompilerException>(() => provider.GetPrimitiveType(
            (PrimitiveTypeCode)0xff));
    }

    [Fact]
    public void ResolvesAssemblyAndNestedTypeReferenceScopes()
    {
        using var assembly = ManagedAssemblyTestFactory.Load(
            typeof(SignatureTypeProviderTests).Assembly.Location);
        var provider = new SignatureTypeProvider(assembly.Identity, assembly.Reader);
        var references = assembly.Reader.TypeReferences
            .Select(handle => provider.GetTypeFromReference(assembly.Reader, handle, 0x12))
            .ToArray();

        Assert.NotEmpty(references);
        Assert.Contains(references, type => type.FullName?.Contains('+') == true);
    }

    [Fact]
    public void ResolvesEnumDefinitionsAndNestedDefinitionNames()
    {
        using var assembly = ManagedAssemblyTestFactory.Load(typeof(DayOfWeek).Assembly.Location);
        var provider = new SignatureTypeProvider(assembly.Identity, assembly.Reader);
        var definitions = assembly.Reader.TypeDefinitions
            .Select(handle => provider.GetTypeFromDefinition(
                assembly.Reader,
                handle,
                0x11))
            .ToArray();

        Assert.Contains(definitions, type => type.FullName == "System.DayOfWeek");
        Assert.Contains(definitions, type => type.FullName?.Contains('+') == true);
    }

    [Fact]
    public void ResolvesEnumDefinitionsUsingTheReaderArgumentWhenNoOwningReaderExists()
    {
        using var assembly = ManagedAssemblyTestFactory.Load(typeof(DayOfWeek).Assembly.Location);
        var provider = new SignatureTypeProvider(assembly.Identity);

        var enumHandle = assembly.Reader.TypeDefinitions.Single(handle =>
            assembly.Reader.GetString(assembly.Reader.GetTypeDefinition(handle).Name) ==
            nameof(DayOfWeek));
        var identity = provider.GetTypeFromDefinition(
            assembly.Reader,
            enumHandle,
            0x11);

        Assert.Equal("System.DayOfWeek", identity.FullName);
    }

    [Fact]
    public void ResolvesTypeReferencesOwnedByTheCurrentModuleOrAnotherModule()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Synthetic.netmodule"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        var module = metadata.AddModuleReference(
            metadata.GetOrAddString("Referenced.netmodule"));
        metadata.AddTypeReference(
            (ModuleDefinitionHandle)MetadataTokens.Handle(1),
            metadata.GetOrAddString(""),
            metadata.GetOrAddString("LocalType"));
        metadata.AddTypeReference(
            module,
            metadata.GetOrAddString("Nested"),
            metadata.GetOrAddString("ReferencedType"));
        var root = new MetadataRootBuilder(metadata);
        var image = new BlobBuilder();
        root.Serialize(image, 1, 0);
        using var provider = MetadataReaderProvider.FromMetadataImage(image.ToImmutableArray());
        var reader = provider.GetMetadataReader();
        var signatureProvider = new SignatureTypeProvider(new("Synthetic"), reader);

        var identities = reader.TypeReferences
            .Select(handle => signatureProvider.GetTypeFromReference(reader, handle, 0x12))
            .ToArray();

        Assert.Contains(identities, identity => identity.FullName == "LocalType");
        Assert.Contains(identities, identity => identity.FullName == "Nested.ReferencedType");
    }

    [Fact]
    public void DoesNotTreatAnUnrelatedTypeDefinitionBaseAsSystemEnum()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Synthetic.netmodule"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        var baseType = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString(""),
            metadata.GetOrAddString("Base"),
            default,
            default,
            default);
        var derivedType = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString(""),
            metadata.GetOrAddString("Derived"),
            baseType,
            default,
            default);
        var root = new MetadataRootBuilder(metadata);
        var image = new BlobBuilder();
        root.Serialize(image, 1, 0);
        using var provider = MetadataReaderProvider.FromMetadataImage(image.ToImmutableArray());
        var reader = provider.GetMetadataReader();

        var identity = new SignatureTypeProvider(new("Synthetic"))
            .GetTypeFromDefinition(reader, derivedType, 0x11);

        Assert.Equal("Derived", identity.FullName);
    }
}
