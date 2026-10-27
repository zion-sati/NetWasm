using System.Text.Json;
using NetWasm.Compiler.ComponentModel.Raw;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Tests.Raw;

public sealed class RawAdapterValueTypeTests
{
    private readonly RawAdapterValuePlanner _wasm32 = new(4);
    private readonly RawAdapterValuePlanner _wasm64 = new(8);

    [Fact]
    public void RejectsAKindItDoesNotKnow()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Type("""{"kind":"stream"}"""));

        Assert.Contains("'stream'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsLayingOutAKindItDoesNotKnow()
    {
        var unknown = new RawAdapterValueType { Kind = (CanonicalAbiTypeKind)int.MaxValue };

        Assert.Throws<InvalidOperationException>(() => _wasm32.Layout(unknown));
    }

    [Theory]
    [InlineData("""{"kind":"unit"}""", 0, 1)]
    [InlineData("""{"kind":"bool"}""", 1, 1)]
    [InlineData("""{"kind":"s16"}""", 2, 2)]
    [InlineData("""{"kind":"character"}""", 4, 4)]
    [InlineData("""{"kind":"borrowed-resource","resourceType":0}""", 4, 4)]
    [InlineData("""{"kind":"f64"}""", 8, 8)]
    [InlineData("""{"kind":"alias","element":{"kind":"u16"}}""", 2, 2)]
    [InlineData("""{"kind":"enum","cases":[{"name":"a","type":null}]}""", 1, 1)]
    [InlineData("""{"kind":"flags","count":8,"flags":[]}""", 1, 1)]
    [InlineData("""{"kind":"flags","count":16,"flags":[]}""", 2, 2)]
    [InlineData("""{"kind":"flags","count":32,"flags":[]}""", 4, 4)]
    [InlineData("""{"kind":"flags","count":33,"flags":[]}""", 8, 4)]
    [InlineData("""{"kind":"record","fields":[{"javascriptName":"a","type":{"kind":"u8"}},{"javascriptName":"b","type":{"kind":"u32"}}]}""", 8, 4)]
    [InlineData("""{"kind":"option","element":{"kind":"u64"}}""", 16, 8)]
    public void LaysOutScalarsAndCompositesAsTheCanonicalAbiDoes(string type, int size, int alignment)
    {
        var layout = _wasm32.Layout(Type(type));

        Assert.Equal((size, alignment), (layout.Size, layout.Alignment));
    }

    [Fact]
    public void LaysOutSequencesAsAnAddressAndALength()
    {
        Assert.Equal((8, 4), Size(_wasm32.Layout(Type("""{"kind":"text"}"""))));
        Assert.Equal((16, 8), Size(_wasm64.Layout(Type("""{"kind":"list","element":{"kind":"u8"}}"""))));
        Assert.Equal(["i32", "i32"], _wasm32.Flatten(Type("""{"kind":"text"}""")));
        Assert.Equal(["i64", "i64"], _wasm64.Flatten(Type("""{"kind":"text"}""")));
    }

    [Theory]
    [InlineData(256, 1)]
    [InlineData(257, 2)]
    [InlineData(65536, 2)]
    [InlineData(65537, 4)]
    public void SizesADiscriminantByItsCaseCount(int cases, int size)
    {
        var names = string.Join(",", Enumerable.Range(0, cases).Select(index => $$"""{"name":"c{{index}}","type":null}"""));
        var enumeration = Type($$"""{"kind":"enum","cases":[{{names}}]}""");
        var variant = Type($$"""{"kind":"variant","cases":[{{names}}]}""");

        Assert.Equal(size, _wasm32.Layout(enumeration).Size);
        Assert.Equal(size, _wasm32.Layout(variant).DiscriminantSize);
    }

    [Theory]
    [InlineData("""{"kind":"unit"}""", "")]
    [InlineData("""{"kind":"s64"}""", "i64")]
    [InlineData("""{"kind":"f32"}""", "f32")]
    [InlineData("""{"kind":"f64"}""", "f64")]
    [InlineData("""{"kind":"owned-resource","resourceType":0}""", "i32")]
    [InlineData("""{"kind":"alias","element":{"kind":"u64"}}""", "i64")]
    [InlineData("""{"kind":"tuple","fields":[{"type":{"kind":"u8"}},{"type":{"kind":"f32"}}]}""", "i32,f32")]
    [InlineData("""{"kind":"flags","count":40,"flags":[]}""", "i32,i32")]
    [InlineData("""{"kind":"flags","count":0,"flags":[]}""", "i32")]
    [InlineData("""{"kind":"result","ok":{"kind":"unit"},"error":{"kind":"f32"}}""", "i32,f32")]
    public void FlattensEachKindToItsCoreValues(string type, string expected)
    {
        Assert.Equal(expected, string.Join(",", _wasm32.Flatten(Type(type))));
    }

    [Theory]
    [InlineData("u32", "u32", "i32")]
    [InlineData("u32", "u64", "i64")]
    [InlineData("u64", "u32", "i64")]
    [InlineData("u32", "f32", "i32")]
    [InlineData("f32", "u32", "i32")]
    [InlineData("u32", "f64", "i64")]
    [InlineData("f64", "u32", "i64")]
    [InlineData("f32", "f32", "f32")]
    [InlineData("f32", "f64", "f64")]
    [InlineData("f64", "f32", "f64")]
    [InlineData("f32", "u64", "i64")]
    [InlineData("f64", "u64", "i64")]
    [InlineData("u64", "f64", "i64")]
    public void JoinsTheCaseSlotsOfAVariant(string first, string second, string joined)
    {
        var variant = Type($$$"""
            {"kind":"variant","cases":[{"name":"a","type":{"kind":"{{{first}}}"}},{"name":"b","type":{"kind":"{{{second}}}"}}]}
            """);

        Assert.Equal(["i32", joined], _wasm32.Flatten(variant));
    }

    [Theory]
    [InlineData("""{"kind":"text"}""", true)]
    [InlineData("""{"kind":"u8"}""", false)]
    [InlineData("""{"kind":"alias","element":{"kind":"text"}}""", true)]
    [InlineData("""{"kind":"option","element":{"kind":"u8"}}""", false)]
    [InlineData("""{"kind":"tuple","fields":[{"type":{"kind":"u8"}},{"type":{"kind":"text"}}]}""", true)]
    [InlineData("""{"kind":"result","ok":null,"error":null}""", false)]
    [InlineData("""{"kind":"result","ok":{"kind":"text"},"error":null}""", true)]
    [InlineData("""{"kind":"result","ok":null,"error":{"kind":"text"}}""", true)]
    [InlineData("""{"kind":"result","ok":{"kind":"u8"},"error":{"kind":"u8"}}""", false)]
    [InlineData("""{"kind":"variant","cases":[{"name":"a","type":null},{"name":"b","type":{"kind":"text"}}]}""", true)]
    [InlineData("""{"kind":"variant","cases":[{"name":"a","type":null},{"name":"b","type":{"kind":"u8"}}]}""", false)]
    public void AllocatesOnlyForValuesThatHoldASequence(string type, bool allocates)
    {
        Assert.Equal(allocates, Type(type).Allocates);
    }

    [Fact]
    public void KeysEqualTypesAlikeAndDifferentTypesApart()
    {
        string[] types =
        [
            """{"kind":"u8"}""",
            """{"kind":"alias","element":{"kind":"u8"}}""",
            """{"kind":"result","ok":null,"error":null}""",
            """{"kind":"result","ok":{"kind":"unit"},"error":null}""",
            """{"kind":"result","ok":null,"error":{"kind":"unit"}}""",
            """{"kind":"variant","cases":[{"name":"a","type":null}]}""",
            """{"kind":"enum","cases":[{"name":"a","type":null}]}""",
            """{"kind":"flags","count":1,"flags":[{"javascriptName":"a"}]}""",
            """{"kind":"owned-resource","resourceType":0}""",
            """{"kind":"borrowed-resource","resourceType":0}""",
            """{"kind":"record","fields":[{"javascriptName":"a","type":{"kind":"u8"}}]}""",
            """{"kind":"tuple","fields":[{"type":{"kind":"u8"}}]}""",
        ];

        var keys = types.Select(type => Type(type).Key).ToArray();

        Assert.Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(keys, types.Select(type => Type(type).Key));
    }

    [Fact]
    public void SeesThroughAliasesToTheUnderlyingKind()
    {
        var alias = Type("""{"kind":"alias","element":{"kind":"alias","element":{"kind":"u8"}}}""");

        Assert.Equal(CanonicalAbiTypeKind.U8, alias.Underlying.Kind);
        Assert.Equal(["i32"], _wasm32.Flatten(alias));
    }

    private static RawAdapterValueType Type(string json)
    {
        using var document = JsonDocument.Parse(json);
        return RawAdapterValueType.Read(document.RootElement);
    }

    private static (int Size, int Alignment) Size(RawAdapterValueLayout layout) => (layout.Size, layout.Alignment);
}
