using System.Collections.Immutable;
using System.Text.Json;
using System.Threading.Tasks;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;

namespace NetWasm.Wit.Bindings.Tests;

public sealed class WitBindingMethodWriterTests
{
    private static readonly string[] IndirectPrimitiveNames =
    [
        "bool", "s8", "u8", "s16", "u16", "s32", "u32",
        "s64", "u64", "f32", "f64", "char", "bool", "u8", "s16", "s32", "u32",
    ];

    [Fact]
    public void ConstructorRejectsMissingSectionCapabilities()
    {
        var imports = WitBindingWriterFixture.CreateImports();
        var exports = WitBindingWriterFixture.CreateExports();
        var flat = WitBindingWriterFixture.CreateFlat();

        Assert.Throws<ArgumentNullException>(() =>
            new WitBindingMethodWriter(null!, exports, flat));
        Assert.Throws<ArgumentNullException>(() =>
            new WitBindingMethodWriter(imports, null!, flat));
        Assert.Throws<ArgumentNullException>(() =>
            new WitBindingMethodWriter(imports, exports, null!));
    }

    [Fact]
    public void OwnedResourceHandleIsCapturedBeforeOwnershipIsTransferred()
    {
        var resource = Type(0, "counter", "\"resource\"");
        var ownedResource = Type(
            1,
            "owned-counter",
            """{"handle":{"own":0}}""");
        var function = new WitFunction(
            "consume",
            [new WitParameter("value", new WitTypeReference.Defined(1))],
            null,
            new WitFunctionKind("freestanding"));
        var @interface = new WitInterface(
            0,
            "api",
            "example:test@1.0.0",
            ImmutableDictionary<string, int>.Empty,
            [function]);
        var world = new WitWorld(
            0,
            "test",
            "example:test@1.0.0",
            [new WitWorldItem("api", 0, null)],
            []);
        var document = new WitDocument(
            [],
            [@interface],
            [world],
            [resource, ownedResource],
            "{}");

        var bindings = WitBindingWriterFixture.Create().Generate(document, world);

        var capture = bindings.IndexOf(
            "var __p0Handle = unchecked((int)value.RawHandle);",
            StringComparison.Ordinal);
        var invalidate = bindings.IndexOf(
            "value.Invalidate();",
            StringComparison.Ordinal);
        var call = bindings.IndexOf("__CanonicalExampleTest100Api_Consume(__p0Handle);",
            StringComparison.Ordinal);

        Assert.True(capture >= 0);
        Assert.True(invalidate > capture);
        Assert.True(call > invalidate);
    }

    private static WitTypeDefinition Type(
        int id,
        string? name,
        string json)
    {
        using var document = JsonDocument.Parse(json);
        return new(id, name, document.RootElement.Clone(), null);
    }

    private static string VariantJson(int count) =>
        VariantJson(Enumerable.Repeat<string?>(null, count).ToArray());

    private static string VariantJson(params string?[] types) =>
        "{\"variant\":{\"cases\":[" +
        string.Join(',', types.Select((type, index) =>
            $"{{\"name\":\"case-{index}\",\"type\":{(type is null ? "null" : $"\"{type}\"")}}}")) + "]}}";

    private static string FlagsJson(int count) =>
        "{\"flags\":{\"flags\":[" +
        string.Join(',', Enumerable.Range(0, count).Select(index =>
            $"{{\"name\":\"flag-{index}\"}}")) + "]}}";

    [Fact]
    public void IndirectParameterOffsetsSelectTheRuntimeAddressWidth()
    {
        var function = new WitFunction(
            "many-values",
            [.. Enumerable.Range(0, 17).Select(index =>
                new WitParameter($"value-{index}",
                    new WitTypeReference.Primitive("string")))],
            null,
            new WitFunctionKind("freestanding"));
        var @interface = new WitInterface(
            0,
            "api",
            "example:test@1.0.0",
            ImmutableDictionary<string, int>.Empty,
            [function]);
        var world = new WitWorld(
            0,
            "test",
            "example:test@1.0.0",
            [],
            [new WitWorldItem("api", 0, null)]);
        var document = new WitDocument([], [@interface], [world], [], "{}");

        var bindings = WitBindingWriterFixture.Create().Generate(document, world);

        Assert.Contains("UIntPtr.Size == 8", bindings);
        Assert.Contains("__parameters +", bindings);
    }

    [Fact]
    public void IndirectPrimitiveParametersLiftFromMemory()
    {
        var function = new WitFunction(
            "many-primitives",
            [.. IndirectPrimitiveNames.Select((name, index) =>
                new WitParameter($"primitive-{index}",
                    new WitTypeReference.Primitive(name)))],
            null,
            new WitFunctionKind("freestanding"));
        var @interface = new WitInterface(
            0,
            "api",
            "example:test@1.0.0",
            ImmutableDictionary<string, int>.Empty,
            [function]);
        var world = new WitWorld(
            0,
            "test",
            "example:test@1.0.0",
            [],
            [new WitWorldItem("api", 0, null)]);
        var document = new WitDocument([], [@interface], [world], [], "{}");

        var bindings = WitBindingWriterFixture.Create().Generate(document, world);

        Assert.Contains("CanonicalAbi.ReadByte", bindings);
        Assert.Contains("CanonicalAbi.ReadDouble", bindings);
    }

    [Theory]
    [InlineData(16, "CanonicalAbi.ReadUInt16")]
    [InlineData(65, "CanonicalAbi.ReadInt32")]
    public void FlagsUseTheCanonicalWordReader(int count, string expectedReader)
    {
        var function = new WitFunction(
            "flags",
            [new WitParameter("value", new WitTypeReference.Defined(0))],
            null,
            new WitFunctionKind("freestanding"));
        var @interface = new WitInterface(
            0,
            "api",
            "example:test@1.0.0",
            ImmutableDictionary<string, int>.Empty.Add("flags", 0),
            [function]);
        var world = new WitWorld(
            0,
            "test",
            "example:test@1.0.0",
            [new WitWorldItem("api", 0, null)],
            [new WitWorldItem("api", 0, null)]);
        var document = new WitDocument(
            [], [@interface], [world],
            [Type(0, "flags", FlagsJson(count))], "{}");

        var bindings = WitBindingWriterFixture.Create().Generate(document, world);

        Assert.Contains(expectedReader, bindings);
    }

    [Fact]
    public void EscapesCSharpKeywordParameterNames()
    {
        var function = new WitFunction(
            "poll",
            [new WitParameter("in", new WitTypeReference.Primitive("u32"))],
            null,
            new WitFunctionKind("freestanding"));
        var @interface = new WitInterface(
            0,
            "poll",
            "wasi:io@0.2.11",
            ImmutableDictionary<string, int>.Empty,
            [function]);
        var world = new WitWorld(
            0,
            "imports",
            "wasi:io@0.2.11",
            [new WitWorldItem("poll", 0, null)],
            []);
        var document = new WitDocument([], [@interface], [world], [], "{}");

        var bindings = WitBindingWriterFixture.Create().Generate(document, world);

        Assert.Contains("public static void Poll(uint @in)", bindings);
        Assert.Contains("unchecked((int)(@in))", bindings);
    }

    [Fact]
    public async Task ConcurrentGenerationKeepsFlatBindingMethodsInvocationLocal()
    {
        var first = RecordResultDocument("first");
        var second = RecordResultDocument("second");
        var writer = WitBindingWriterFixture.Create();

        var results = await Task.WhenAll(Enumerable.Range(0, 64).Select(index =>
            Task.Run(() =>
            {
                var source = index % 2 == 0 ? first : second;
                return writer.Generate(source.Document, source.World);
            })));

        for (var index = 0; index < results.Length; index++)
        {
            var own = index % 2 == 0 ? "First" : "Second";
            var other = index % 2 == 0 ? "Second" : "First";
            Assert.Contains($"public static {own} Read()", results[index]);
            Assert.Contains($"internal static {own} LiftType0", results[index]);
            Assert.DoesNotContain(other, results[index]);
        }
    }

    [Fact]
    public void EmitsPrimitiveAggregateVariantResourceAndIndirectBindings()
    {
        var (document, world) = BindingShapeDocument();

        var bindings = WitBindingWriterFixture.Create().Generate(document, world);

        Assert.Contains("public static float Direct(", bindings);
        Assert.Contains("CanonicalAbi.LowerString", bindings);
        Assert.Contains("CanonicalAbi.LiftBoolean", bindings);
        Assert.Contains("var __parameterBlock = CanonicalAbi.Allocate", bindings);
        Assert.Contains("IndirectPrimitives", bindings);
        Assert.Contains("WriteByte", bindings);
        Assert.Contains("LiftDiscriminant", bindings);
        Assert.Contains("__CanonicalMarshalling.FreeType", bindings);
        Assert.Contains("LowerExport()", bindings);
        Assert.Contains("Invalidate();", bindings);
    }

    private static (WitDocument Document, WitWorld World) RecordResultDocument(
        string typeName)
    {
        var type = Type(
            0,
            typeName,
            """{"record":{"fields":[{"name":"value","type":"s32"}]}}""");
        var function = new WitFunction(
            "read",
            [],
            new WitTypeReference.Defined(0),
            new WitFunctionKind("freestanding"));
        var @interface = new WitInterface(
            0,
            "api",
            "example:test@1.0.0",
            ImmutableDictionary<string, int>.Empty.Add(typeName, 0),
            [function]);
        var world = new WitWorld(
            0,
            "test",
            "example:test@1.0.0",
            [new WitWorldItem("api", 0, null)],
            []);
        return (new WitDocument([], [@interface], [world], [type], "{}"), world);
    }

    private static (WitDocument Document, WitWorld World) BindingShapeDocument()
    {
        var types = ImmutableArray.Create(
            Type(0, "text-list", "{\"list\":\"string\"}"),
            Type(1, "pair", "{\"tuple\":{\"types\":[\"s32\",\"string\"]}}"),
            Type(2, "maybe-text", "{\"option\":\"string\"}"),
            Type(3, "outcome", "{\"result\":{\"ok\":\"string\",\"err\":null}}"),
            Type(4, "choice", "{\"variant\":{\"cases\":[{\"name\":\"none\",\"type\":null},{\"name\":\"text\",\"type\":\"string\"},{\"name\":\"count\",\"type\":\"s64\"}]}}"),
            Type(5, "permissions", "{\"flags\":{\"flags\":[{\"name\":\"a\"},{\"name\":\"b\"},{\"name\":\"c\"},{\"name\":\"d\"},{\"name\":\"e\"},{\"name\":\"f\"},{\"name\":\"g\"},{\"name\":\"h\"},{\"name\":\"i\"}]}}"),
            Type(6, "envelope", "{\"record\":{\"fields\":[{\"name\":\"list\",\"type\":0},{\"name\":\"pair\",\"type\":1},{\"name\":\"maybe\",\"type\":2},{\"name\":\"outcome\",\"type\":3},{\"name\":\"choice\",\"type\":4},{\"name\":\"permissions\",\"type\":5}]}}"),
            Type(7, "counter", "\"resource\""),
            Type(8, "owned-counter", "{\"handle\":{\"own\":7}}"),
            Type(9, "borrowed-counter", "{\"handle\":{\"borrow\":7}}"),
            Type(10, "small-permissions", "{\"flags\":{\"flags\":[{\"name\":\"a\"},{\"name\":\"b\"}]}}"),
            Type(11, "single", "{\"type\":\"f32\"}"),
            Type(12, "double", "{\"type\":\"f64\"}"),
            Type(13, "small-enum", "{\"enum\":{\"cases\":[{\"name\":\"a\"},{\"name\":\"b\"}]}}"),
            Type(14, "large-variant", VariantJson(257)),
            Type(15, "variant-f32", VariantJson(null, "f32")),
            Type(16, "variant-f64", VariantJson(null, "f64")),
            Type(17, "variant-f32-i32", VariantJson("f32", "s32")),
            Type(18, "variant-i32-i64", VariantJson("s32", "s64")),
            Type(19, "variant-f32-i64", VariantJson("f32", "s64")),
            Type(20, "variant-f64-i64", VariantJson("f64", "s64")),
            Type(21, "variant-f32-f64", VariantJson("f32", "f64")),
            Type(22, "variant-text-i32", VariantJson("string", "s32")),
            Type(23, "variant-text-i64", VariantJson("string", "s64")),
            Type(24, "variant-text-f32", VariantJson("string", "f32")),
            Type(25, "huge-variant", VariantJson(65537)),
            Type(26, "medium-permissions", FlagsJson(16)),
            Type(27, "wide-permissions", FlagsJson(65)));
        var functions = new[]
        {
            new WitFunction(
                "direct",
                [
                    new WitParameter("bool-value", new WitTypeReference.Primitive("bool")),
                    new WitParameter("s8-value", new WitTypeReference.Primitive("s8")),
                    new WitParameter("u8-value", new WitTypeReference.Primitive("u8")),
                    new WitParameter("s16-value", new WitTypeReference.Primitive("s16")),
                    new WitParameter("u16-value", new WitTypeReference.Primitive("u16")),
                    new WitParameter("s32-value", new WitTypeReference.Primitive("s32")),
                    new WitParameter("u32-value", new WitTypeReference.Primitive("u32")),
                    new WitParameter("s64-value", new WitTypeReference.Primitive("s64")),
                    new WitParameter("u64-value", new WitTypeReference.Primitive("u64")),
                    new WitParameter("f32-value", new WitTypeReference.Primitive("f32")),
                    new WitParameter("f64-value", new WitTypeReference.Primitive("f64")),
                    new WitParameter("char-value", new WitTypeReference.Primitive("char")),
                    new WitParameter("text-value", new WitTypeReference.Primitive("string")),
                ],
                new WitTypeReference.Primitive("f32"),
                new WitFunctionKind("freestanding")),
            new WitFunction(
                "indirect",
                [.. Enumerable.Range(0, 17).Select(index =>
                    new WitParameter($"text-{index}", new WitTypeReference.Primitive("string")))],
                new WitTypeReference.Defined(6),
                new WitFunctionKind("freestanding")),
            new WitFunction(
                "indirect-primitives",
                [.. IndirectPrimitiveNames.Select((name, index) =>
                    new WitParameter($"primitive-{index}",
                        new WitTypeReference.Primitive(name)))],
                null,
                new WitFunctionKind("freestanding")),
            new WitFunction(
                "shapes",
                [
                    new WitParameter("pair", new WitTypeReference.Defined(1)),
                    new WitParameter("maybe", new WitTypeReference.Defined(2)),
                    new WitParameter("choice", new WitTypeReference.Defined(4)),
                    new WitParameter("permissions", new WitTypeReference.Defined(5)),
                ],
                new WitTypeReference.Defined(4),
                new WitFunctionKind("freestanding")),
            new WitFunction(
                "resources",
                [
                    new WitParameter("owned", new WitTypeReference.Defined(8)),
                    new WitParameter("borrowed", new WitTypeReference.Defined(9)),
                ],
                new WitTypeReference.Defined(8),
                new WitFunctionKind("freestanding")),
            new WitFunction(
                "borrowed-result",
                [],
                new WitTypeReference.Defined(9),
                new WitFunctionKind("freestanding")),
            new WitFunction(
                "flags-result",
                [],
                new WitTypeReference.Defined(5),
                new WitFunctionKind("freestanding")),
            new WitFunction(
                "list-round",
                [new WitParameter("values", new WitTypeReference.Defined(0))],
                null,
                new WitFunctionKind("freestanding")),
            new WitFunction(
                "text-result",
                [],
                new WitTypeReference.Primitive("string"),
                new WitFunctionKind("freestanding")),
            new WitFunction(
                "list-result",
                [],
                new WitTypeReference.Defined(0),
                new WitFunctionKind("freestanding")),
            new WitFunction(
                "aliases",
                [
                    new WitParameter("single", new WitTypeReference.Defined(11)),
                    new WitParameter("double", new WitTypeReference.Defined(12)),
                    new WitParameter("small", new WitTypeReference.Defined(10)),
                    new WitParameter("medium", new WitTypeReference.Defined(26)),
                    new WitParameter("wide", new WitTypeReference.Defined(27)),
                ],
                null,
                new WitFunctionKind("freestanding")),
            new WitFunction(
                "many-lists",
                [.. Enumerable.Range(0, 9).Select(index =>
                    new WitParameter($"list-{index}", new WitTypeReference.Defined(0)))],
                null,
                new WitFunctionKind("freestanding")),
            new WitFunction(
                "many-resources",
                [.. Enumerable.Range(0, 17).Select(index =>
                    new WitParameter($"resource-{index}", new WitTypeReference.Defined(8)))],
                null,
                new WitFunctionKind("freestanding")),
            new WitFunction(
                "many-borrowed",
                [.. Enumerable.Range(0, 17).Select(index =>
                    new WitParameter($"borrowed-{index}", new WitTypeReference.Defined(9)))],
                null,
                new WitFunctionKind("freestanding")),
            new WitFunction(
                "many-envelopes",
                [.. Enumerable.Range(0, 17).Select(index =>
                    new WitParameter($"envelope-{index}", new WitTypeReference.Defined(6)))],
                null,
                new WitFunctionKind("freestanding")),
            new WitFunction(
                "variant-shapes",
                [
                    new WitParameter("enum", new WitTypeReference.Defined(13)),
                    new WitParameter("large", new WitTypeReference.Defined(14)),
                    new WitParameter("f32", new WitTypeReference.Defined(15)),
                    new WitParameter("f64", new WitTypeReference.Defined(16)),
                    new WitParameter("f32-i32", new WitTypeReference.Defined(17)),
                    new WitParameter("i32-i64", new WitTypeReference.Defined(18)),
                    new WitParameter("f32-i64", new WitTypeReference.Defined(19)),
                    new WitParameter("f64-i64", new WitTypeReference.Defined(20)),
                ],
                null,
                new WitFunctionKind("freestanding")),
            new WitFunction(
                "variant-address-shapes",
                [
                    new WitParameter("f32-f64", new WitTypeReference.Defined(21)),
                    new WitParameter("text-i32", new WitTypeReference.Defined(22)),
                    new WitParameter("text-i64", new WitTypeReference.Defined(23)),
                    new WitParameter("text-f32", new WitTypeReference.Defined(24)),
                ],
                null,
                new WitFunctionKind("freestanding")),
            new WitFunction(
                "result-param",
                [new WitParameter("result", new WitTypeReference.Defined(3))],
                null,
                new WitFunctionKind("freestanding")),
            new WitFunction(
                "huge-variant-param",
                [new WitParameter("value", new WitTypeReference.Defined(25))],
                null,
                new WitFunctionKind("freestanding")),
        };
        var @interface = new WitInterface(
            0,
            "api",
            "example:test@1.0.0",
            types.Where(type => type.Name is not null)
                .ToImmutableDictionary(type => type.Name!, type => type.Id),
            [.. functions]);
        var world = new WitWorld(
            0,
            "test",
            "example:test@1.0.0",
            [new WitWorldItem("api", 0, null)],
            [new WitWorldItem("api", 0, null)]);
        return (new WitDocument([], [@interface], [world], types, "{}"), world);
    }

}
