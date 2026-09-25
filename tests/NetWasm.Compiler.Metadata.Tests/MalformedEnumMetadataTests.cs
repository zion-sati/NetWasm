using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class MalformedEnumMetadataTests
{
    private static readonly byte[] Int32FieldSignature = [2, 6, 8];
    private static readonly byte[] LiteralBlob = [4, 0x13, 0x37, 0xC0, 0xDE];

    [Fact]
    public void LoaderAcceptsCharBackedEnumMetadata()
    {
        var image = CompileEnumImage();
        ReplaceUnique(image, Int32FieldSignature, 2, 3);

        using var assembly = ManagedAssembly.Parse(
            "char-enum.dll",
            image,
            AssemblyIdentityAliases.Empty,
            new ValueTypeDefinitionStackKindResolver());

        var definition = assembly.Types.Values.Single(type => type.Name == "Probe");
        Assert.Equal("primitive:char", definition.EnumUnderlyingType.CanonicalName);
        Assert.Equal(0x3713UL, Assert.Single(definition.EnumMembers).RawValue);
    }

    [Fact]
    public void LoaderRejectsUnsupportedEnumUnderlyingMetadata()
    {
        var image = CompileEnumImage();
        ReplaceUnique(image, Int32FieldSignature, 2, 12);

        var exception = Assert.Throws<CompilerException>(() =>
            ManagedAssembly.Parse(
                "float-enum.dll",
                image,
                AssemblyIdentityAliases.Empty,
                new ValueTypeDefinitionStackKindResolver()));

        Assert.Contains("enum has unsupported underlying type 'primitive:f4'", exception.Message);
    }

    [Fact]
    public void LoaderRejectsTrailingAndTruncatedLiteralBlobs()
    {
        var trailing = CompileEnumImage();
        ReplaceUnique(trailing, LiteralBlob, 0, 5);
        var trailingException = Assert.Throws<CompilerException>(() =>
            ManagedAssembly.Parse(
                "trailing-enum.dll",
                trailing,
                AssemblyIdentityAliases.Empty,
                new ValueTypeDefinitionStackKindResolver()));

        var truncated = CompileEnumImage();
        ReplaceUnique(truncated, LiteralBlob, 0, 3);
        var truncatedException = Assert.Throws<CompilerException>(() =>
            ManagedAssembly.Parse(
                "truncated-enum.dll",
                truncated,
                AssemblyIdentityAliases.Empty,
                new ValueTypeDefinitionStackKindResolver()));

        Assert.Contains("trailing literal bytes", trailingException.Message);
        Assert.Contains("truncated metadata constant", truncatedException.Message);
    }

    [Fact]
    public void LoaderRejectsEnumMembersWithoutMetadataConstants()
    {
        var exception = Assert.Throws<CompilerException>(() =>
            ManagedAssembly.Parse(
                "missing-enum-constant.dll",
                BuildMissingConstantImage(),
                AssemblyIdentityAliases.Empty,
                new ValueTypeDefinitionStackKindResolver()));

        Assert.Contains("enum member 'Missing' has no metadata constant", exception.Message);
    }

    private static byte[] CompileEnumImage()
    {
        using var assets = TestAssets.Create();
        var path = assets.CompileSource(
            "MalformedEnumMetadata",
            """
            public enum Probe : int
            {
                Value = unchecked((int)0xDEC03713),
            }
            """);
        return File.ReadAllBytes(path);
    }

    private static byte[] BuildMissingConstantImage()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("MissingEnumConstant.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("MissingEnumConstant"),
            new Version(1, 0, 0, 0),
            default,
            default,
            (AssemblyFlags)0,
            AssemblyHashAlgorithm.None);
        var coreLibrary = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Private.CoreLib"),
            new Version(10, 0, 0, 0),
            default,
            default,
            (AssemblyFlags)0,
            default);
        var enumBase = metadata.AddTypeReference(
            coreLibrary,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Enum"));
        var firstField = MetadataTokens.FieldDefinitionHandle(1);
        var firstMethod = MetadataTokens.MethodDefinitionHandle(1);
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            firstField,
            firstMethod);
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            default,
            metadata.GetOrAddString("Broken"),
            enumBase,
            firstField,
            firstMethod);
        var signature = new BlobBuilder();
        signature.WriteByte(6);
        signature.WriteByte(8);
        var signatureHandle = metadata.GetOrAddBlob(signature);
        metadata.AddFieldDefinition(
            FieldAttributes.Public | FieldAttributes.SpecialName |
                FieldAttributes.RTSpecialName,
            metadata.GetOrAddString("value__"),
            signatureHandle);
        metadata.AddFieldDefinition(
            FieldAttributes.Public | FieldAttributes.Static,
            metadata.GetOrAddString("Missing"),
            signatureHandle);

        var pe = new ManagedPEBuilder(
            new PEHeaderBuilder(
                imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            new MetadataRootBuilder(metadata),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    private static void ReplaceUnique(
        byte[] image,
        byte[] pattern,
        int relativeOffset,
        byte replacement)
    {
        var match = -1;
        for (var offset = 0; offset <= image.Length - pattern.Length; offset++)
        {
            if (!image.AsSpan(offset, pattern.Length).SequenceEqual(pattern))
                continue;
            Assert.Equal(-1, match);
            match = offset;
        }

        Assert.NotEqual(-1, match);
        image[match + relativeOffset] = replacement;
    }
}
