using System.Collections.Immutable;
using System.Text.Json;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;

namespace NetWasm.Wit.Bindings.Tests;

public sealed class WitCanonicalMarshallingWriterTests
{
    private static readonly string[] PrimitiveTypes =
        ["bool", "s8", "u8", "s16", "u16", "s32", "u32", "s64", "u64", "f32", "f64", "char", "string"];

    [Fact]
    public void ConstructorRejectsMissingTypeSectionCapability()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new WitCanonicalMarshallingWriter(null!));
    }

    [Fact]
    public void NestedResourcesUseBoundarySpecificOwnershipSemantics()
    {
        var resource = Type(0, "counter", "\"resource\"");
        var owned = Type(1, null, """{"handle":{"own":0}}""");
        var borrowed = Type(2, null, """{"handle":{"borrow":0}}""");
        var envelope = Type(
            3,
            "envelope",
            """{"record":{"fields":[{"name":"owned","type":1},{"name":"borrowed","type":2}]}}""");
        var function = new WitFunction(
            "exchange",
            [new WitParameter("value", new WitTypeReference.Defined(3))],
            new WitTypeReference.Defined(3),
            new WitFunctionKind("freestanding"));
        var @interface = new WitInterface(
            0,
            "resources",
            "example:test@1.0.0",
            ImmutableDictionary<string, int>.Empty
                .Add("counter", 0)
                .Add("envelope", 3),
            [function]);
        var world = new WitWorld(
            0,
            "test",
            "example:test@1.0.0",
            [new WitWorldItem("resources", 0, null)],
            []);
        var document = new WitDocument(
            [],
            [@interface],
            [world],
            [resource, owned, borrowed, envelope],
            "{}");

        var source = WitCanonicalMarshallingWriterFixture.Create()
            .Generate(document, world);

        Assert.Contains(
            "internal static CanonicalBuffer LowerType3(Envelope value, bool exportBoundary)",
            source);
        Assert.Contains(
            "exportBoundary ? value.Owned.LowerExport() : value.Owned.LowerImport(true)",
            source);
        Assert.Contains(
            "exportBoundary ? value.Borrowed.LowerExport() : value.Borrowed.LowerImport(false)",
            source);
        Assert.Contains(
            "exportBoundary ? Counter.LiftExport(",
            source);
        Assert.Contains(
            ", true) : new Counter(",
            source);
        Assert.Contains(", false) : new Counter(",
            source);
    }

    [Fact]
    public void EmitsAllCanonicalAggregateShapesAndFlagWidths()
    {
        var types = ImmutableArray.Create(
            Type(0, "counter", "\"resource\""),
            Type(1, "text-list", "{\"list\":\"string\"}"),
            Type(2, "pair", "{\"tuple\":{\"types\":[\"s32\",\"string\"]}}"),
            Type(3, "maybe-text", "{\"option\":\"string\"}"),
            Type(4, "outcome", "{\"result\":{\"ok\":\"string\",\"err\":null}}"),
            Type(5, "choice", "{\"variant\":{\"cases\":[{\"name\":\"none\",\"type\":null},{\"name\":\"text\",\"type\":\"string\"},{\"name\":\"count\",\"type\":\"s64\"}]}}"),
            Type(6, "color", "{\"enum\":{\"cases\":[{\"name\":\"red\"},{\"name\":\"blue\"}]}}"),
            Type(7, "small-flags", "{\"flags\":{\"flags\":[{\"name\":\"a\"},{\"name\":\"b\"}]}}"),
            Type(8, "medium-flags", "{\"flags\":{\"flags\":[{\"name\":\"a\"},{\"name\":\"b\"},{\"name\":\"c\"},{\"name\":\"d\"},{\"name\":\"e\"},{\"name\":\"f\"},{\"name\":\"g\"},{\"name\":\"h\"},{\"name\":\"i\"}]}}"),
            Type(9, "wide-flags", FlagsJson(65)),
            Type(10, "alias-text", "{\"type\":\"string\"}"),
            Type(11, "owned-counter", "{\"handle\":{\"own\":0}}"),
            Type(12, "borrowed-counter", "{\"handle\":{\"borrow\":0}}"),
            Type(13, "handles", "{\"record\":{\"fields\":[{\"name\":\"owned\",\"type\":11},{\"name\":\"borrowed\",\"type\":12}]}}"),
            Type(14, "primitive-pack", PrimitivePackJson()),
            Type(15, "flags32", FlagsJson(32)),
            Type(16, "flags64", FlagsJson(64)),
            Type(17, "large-enum", EnumJson(257)),
            Type(18, "huge-enum", EnumJson(65537)),
            Type(19, "everything", "{\"record\":{\"fields\":[{\"name\":\"list\",\"type\":1},{\"name\":\"pair\",\"type\":2},{\"name\":\"maybe\",\"type\":3},{\"name\":\"result\",\"type\":4},{\"name\":\"choice\",\"type\":5},{\"name\":\"color\",\"type\":6},{\"name\":\"small\",\"type\":7},{\"name\":\"medium\",\"type\":8},{\"name\":\"wide\",\"type\":9},{\"name\":\"alias\",\"type\":10},{\"name\":\"handles\",\"type\":13},{\"name\":\"primitives\",\"type\":14},{\"name\":\"flags32\",\"type\":15},{\"name\":\"flags64\",\"type\":16},{\"name\":\"large\",\"type\":17},{\"name\":\"huge\",\"type\":18},{\"name\":\"resource\",\"type\":0}]}}"));
        var function = new WitFunction(
            "round-trip",
            [new WitParameter("value", new WitTypeReference.Defined(19))],
            new WitTypeReference.Defined(19),
            new WitFunctionKind("freestanding"));
        var @interface = new WitInterface(
            0,
            "api",
            "example:test@1.0.0",
            types.Where(type => type.Name is not null)
                .ToImmutableDictionary(type => type.Name!, type => type.Id),
            [function]);
        var world = new WitWorld(
            0,
            "test",
            "example:test@1.0.0",
            [new WitWorldItem("api", 0, null)],
            []);
        var document = new WitDocument([], [@interface], [world], types, "{}");

        var source = WitCanonicalMarshallingWriterFixture.Create()
            .Generate(document, world);

        Assert.Contains("LowerType1(string[] value", source);
        Assert.Contains("HasValue ? (byte)1", source);
        Assert.Contains("IsOk ? (byte)0", source);
        Assert.Contains("switch (value.Choice.Tag)", source);
        Assert.Contains("CanonicalAbi.WriteUInt16(", source);
        Assert.Contains("CanonicalAbi.WriteInt32(", source);
        Assert.Contains(".GetWord(index)", source);
        Assert.Contains("LiftFlagsWord", source);
        Assert.Contains("FreeType1(CanonicalAbi.ReadAddress", source);
    }

    private static WitTypeDefinition Type(
        int id,
        string? name,
        string json)
    {
        using var document = JsonDocument.Parse(json);
        return new(id, name, document.RootElement.Clone(), null);
    }

    private static string FlagsJson(int count) =>
        "{\"flags\":{\"flags\":[" +
        string.Join(',', Enumerable.Range(0, count).Select(index =>
            $"{{\"name\":\"flag-{index}\"}}")) + "]}}";

    private static string PrimitivePackJson() =>
        "{\"record\":{\"fields\":[" +
        string.Join(',', PrimitiveTypes
        .Select((type, index) => $"{{\"name\":\"value-{index}\",\"type\":\"{type}\"}}")) + "]}}";

    private static string EnumJson(int count) =>
        "{\"enum\":{\"cases\":[" +
        string.Join(',', Enumerable.Range(0, count).Select(index =>
            $"{{\"name\":\"case-{index}\"}}")) + "]}}";
}
