using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class ManagedAssemblyLoaderTests
{
    [Fact]
    public void LoadsAnAssemblyFromAnInjectedImageReader()
    {
        using var assets = TestAssets.Create();
        var image = File.ReadAllBytes(assets.Application);
        var reader = new RecordingImageReader(image);
        var loader = new ManagedAssemblyLoader(reader, new ValueTypeDefinitionStackKindResolver());

        using var assembly = loader.Load("memory/application.dll");

        Assert.Equal("Fixture.Application", assembly.Identity.Name);
        Assert.Equal("memory/application.dll", reader.Path);
    }

    [Fact]
    public void ValidatesInjectedImageReader()
    {
        Assert.Throws<ArgumentNullException>(() => new ManagedAssemblyLoader(null!, new ValueTypeDefinitionStackKindResolver()));
        Assert.Throws<ArgumentNullException>(() =>
            new ManagedAssemblyLoader(new ManagedAssemblyImageReader(), null!));
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
}
