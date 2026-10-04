using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.NativeInterop;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class ManagedAssemblyLoaderTests
{
    [Fact]
    public void LoadPreservesFactsFromTheInjectedNativeDeclarationCapability()
    {
        var image = CreateNativeImage();
        var images = new RecordingImageReader(image);
        var declaration = new NativeImportDeclaration("selected-library", "selected-entry",
            MethodImportAttributes.CallingConventionCDecl, false, false, false, false);
        var native = new RecordingNativeReader(declaration);
        var stackKinds = new RecordingStackKindResolver();
        var loader = Assert.IsAssignableFrom<IManagedAssemblyLoader>(
            new ManagedAssemblyLoader(
                images,
                stackKinds,
                native,
                new NativeCallbackDeclarationReader()));

        using var assembly = loader.Load("memory/native.dll", ImmutableDictionary<string, string>.Empty);

        var method = Assert.Single(assembly.Methods.Values);
        Assert.Same(declaration, method.NativeImport);
        Assert.Equal("NativeFixture", assembly.Identity.Name);
        Assert.Equal("memory/native.dll", images.Path);
        Assert.Equal("Call", native.MethodName);
        Assert.Equal(1, native.Calls);
        Assert.Equal(["<Module>", "Native"], stackKinds.Names);
        Assert.True(method.IsStatic);
        Assert.False(method.HasBody);
        Assert.Equal(CliValueKind.I4, method.Signature.ReturnType);
    }

    [Fact]
    public void LoadsAnAssemblyFromAnInjectedImageReader()
    {
        using var assets = TestAssets.Create();
        var image = File.ReadAllBytes(assets.Application);
        var reader = new RecordingImageReader(image);
        var loader = new ManagedAssemblyLoader(
            reader,
            new ValueTypeDefinitionStackKindResolver(),
            new NativeImportDeclarationReader(),
            new NativeCallbackDeclarationReader());

        using var assembly = loader.Load("memory/application.dll");

        Assert.Equal("Fixture.Application", assembly.Identity.Name);
        Assert.Equal("memory/application.dll", reader.Path);
    }

    [Fact]
    public void ValidatesInjectedImageReader()
    {
        Assert.Throws<ArgumentNullException>(() => new ManagedAssemblyLoader(
            null!,
            new ValueTypeDefinitionStackKindResolver(),
            new NativeImportDeclarationReader(),
            new NativeCallbackDeclarationReader()));
        Assert.Throws<ArgumentNullException>(() =>
            new ManagedAssemblyLoader(
                new ManagedAssemblyImageReader(),
                null!,
                new NativeImportDeclarationReader(),
                new NativeCallbackDeclarationReader()));
        Assert.Throws<ArgumentNullException>(() =>
            new ManagedAssemblyLoader(
                new ManagedAssemblyImageReader(),
                new ValueTypeDefinitionStackKindResolver(),
                null!,
                new NativeCallbackDeclarationReader()));
        Assert.Throws<ArgumentNullException>(() =>
            new ManagedAssemblyLoader(
                new ManagedAssemblyImageReader(),
                new ValueTypeDefinitionStackKindResolver(),
                new NativeImportDeclarationReader(),
                null!));
        Assert.Throws<ArgumentException>(() => new ManagedAssemblyImageReader().Read(""));
    }

    [Fact]
    public void ExtractsEnumConstantsAndFlagsWithoutLoadingTypes()
    {
        using var assets = TestAssets.Create();
        var path = assets.CompileSource(
            "EnumMetadata",
            """
            using System;
            [Flags]
            public enum Access : byte
            {
                None = 0,
                Read = 1,
                High = 128,
            }
            public enum Signed : sbyte
            {
                Negative = -1,
            }
            public enum Signed16 : short { Value = -1 }
            public enum Unsigned16 : ushort { Value = 65535 }
            public enum Signed32 : int { Value = -1 }
            public enum Unsigned32 : uint { Value = uint.MaxValue }
            public enum Signed64 : long { Value = -1 }
            public enum Unsigned64 : ulong { Value = ulong.MaxValue }
            public static class Constants { public const string Text = "text"; }
            """);

        using var assembly = ManagedAssemblyTestFactory.Load(path);

        var access = assembly.Types.Values.Single(type => type.Name == "Access");
        Assert.True(access.IsEnum);
        Assert.True(access.IsFlagsEnum);
        Assert.Equal("primitive:u1", access.EnumUnderlyingType.CanonicalName);
        Assert.Equal(
            new[] { ("None", 0UL), ("Read", 1UL), ("High", 128UL) },
            access.EnumMembers.Select(member => (member.Name, member.RawValue)));

        var signed = assembly.Types.Values.Single(type => type.Name == "Signed");
        Assert.Equal("primitive:i1", signed.EnumUnderlyingType.CanonicalName);
        Assert.Equal(255UL, Assert.Single(signed.EnumMembers).RawValue);

        Assert.Equal(ushort.MaxValue, Member("Signed16"));
        Assert.Equal(ushort.MaxValue, Member("Unsigned16"));
        Assert.Equal(uint.MaxValue, Member("Signed32"));
        Assert.Equal(uint.MaxValue, Member("Unsigned32"));
        Assert.Equal(ulong.MaxValue, Member("Signed64"));
        Assert.Equal(ulong.MaxValue, Member("Unsigned64"));

        ulong Member(string typeName) => Assert.Single(
            assembly.Types.Values.Single(type => type.Name == typeName).EnumMembers).RawValue;
    }

    private sealed class RecordingImageReader(byte[] image) : IManagedAssemblyImageReader
    {
        private readonly byte[] _image = image;

        public string? Path { get; private set; }

        public byte[] Read(string path)
        {
            Path = path;
            return _image;
        }
    }

    private static byte[] CreateNativeImage()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString("NativeFixture.dll"),
            metadata.GetOrAddGuid(Guid.Empty), default, default);
        metadata.AddAssembly(metadata.GetOrAddString("NativeFixture"), new(1, 0), default, default, 0, 0);
        metadata.AddTypeDefinition(TypeAttributes.NotPublic, default, metadata.GetOrAddString("<Module>"),
            default, MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(TypeAttributes.Public, metadata.GetOrAddString("Fixture"),
            metadata.GetOrAddString("Native"), default,
            MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        var signature = new BlobBuilder();
        new BlobEncoder(signature).MethodSignature().Parameters(0, result => result.Type().Int32(), _ => { });
        var method = metadata.AddMethodDefinition(MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.PinvokeImpl,
            MethodImplAttributes.IL, metadata.GetOrAddString("Call"), metadata.GetOrAddBlob(signature),
            -1, MetadataTokens.ParameterHandle(1));
        metadata.AddMethodImport(method, MethodImportAttributes.CallingConventionCDecl,
            metadata.GetOrAddString("entry"), metadata.AddModuleReference(metadata.GetOrAddString("library")));
        var image = new BlobBuilder();
        new ManagedPEBuilder(new(imageCharacteristics: Characteristics.Dll),
            new(metadata), new BlobBuilder()).Serialize(image);
        return image.ToArray();
    }

    private sealed class RecordingNativeReader(NativeImportDeclaration declaration) : INativeImportDeclarationReader
    {
        public int Calls { get; private set; }
        public string? MethodName { get; private set; }

        public NativeImportDeclaration? Read(MetadataReader metadata, MethodDefinition method)
        {
            Calls++;
            MethodName = metadata.GetString(method.Name);
            return declaration;
        }
    }

    private sealed class RecordingStackKindResolver : IValueTypeDefinitionStackKindResolver
    {
        public List<string> Names { get; } = [];

        public CliValueKind Resolve(string canonicalName)
        {
            Names.Add(canonicalName);
            return CliValueKind.ValueType;
        }
    }
}
