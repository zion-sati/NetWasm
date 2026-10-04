using System.Text;
using System.Text.Json.Nodes;
using NetWasm.Hosting.Deployment;

namespace NetWasm.Hosting.Tests.Deployment;

public sealed class WitWorkerComponentAdapterWriterTests
{
    private const string JcoVersion = CanonicalComponentAdapterWriter.SupportedJcoVersion;
    private static readonly string[] PrimitiveTypes =
    [
        "bool", "s8", "u8", "s16", "u16", "s32", "u32", "s64", "u64",
        "f32", "f64", "char", "string",
    ];
    private readonly WitWorkerComponentAdapterWriter _subject = new(WitWorkerContractComposition.CreateReader(), new WitWorkerValueLayoutPlanner());

    [Fact]
    public void RestoresDeclaredResultsThroughAliasesAndRequiresCapabilities()
    {
        var fixture = WitWorkerContractReaderTests.Fixture();
        fixture["types"] = JsonNode.Parse("[{\"id\":42,\"kind\":{\"type\":43}},{\"id\":43,\"kind\":{\"result\":{\"ok\":\"u32\",\"err\":\"string\"}}}]");
        var js = Encoding.UTF8.GetString(_subject.Write(new(WitWorkerContractReaderTests.Bytes(fixture), JcoVersion, [new("echo", "function")])));
        Assert.Contains("generatedModule._util?.isComponentError", js, StringComparison.Ordinal);
        Assert.Contains("if (!isComponentError(cause)) throw cause", js, StringComparison.Ordinal);
        Assert.Contains("tag: 'ok'", js, StringComparison.Ordinal); Assert.Contains("tag: 'err'", js, StringComparison.Ordinal);
        fixture["types"]![1]!["kind"] = JsonNode.Parse("{\"type\":\"u32\"}");
        js = Encoding.UTF8.GetString(_subject.Write(new(WitWorkerContractReaderTests.Bytes(fixture), JcoVersion, [new("echo", "function")])));
        Assert.DoesNotContain("isComponentError", js, StringComparison.Ordinal);
        Assert.Throws<ArgumentNullException>(() => new WitWorkerComponentAdapterWriter(null!, new WitWorkerValueLayoutPlanner()));
        Assert.Throws<ArgumentNullException>(() => new WitWorkerComponentAdapterWriter(WitWorkerContractComposition.CreateReader(), null!));
    }

    [Fact]
    public void WritesDeterministicExactRootAndInterfaceBindings()
    {
        var contract = JsonNode.Parse(Contract(
            Export(
                "example:worker/math-api@1.0.0/add-value",
                "interface",
                "math-alias",
                "example:worker/math-api@1.0.0",
                "mathAlias",
                "addValue",
                "add-value",
                AllPrimitiveParameters(),
                Type("s32")),
            Export(
                "root-value",
                "root",
                "root-value",
                null,
                "",
                "rootValue",
                "root-value",
                [],
                null)))!.AsObject();
        contract["reactor"] = Reactor();

        var contractJson = contract.ToJsonString();
        WitWorkerRootExport[] roots =
        [
            new("mathAlias", "instance"),
            new("rootValue", "function"),
            new("netwasm:runtime/reactor-guest@1.0.0", "instance"),
        ];
        var first = _subject.Write(new(
            Encoding.UTF8.GetBytes(contractJson), JcoVersion, [.. roots]));
        var second = _subject.Write(new(
            Encoding.UTF8.GetBytes(contractJson), JcoVersion, [.. roots]));
        var source = Encoding.UTF8.GetString(first);

        Assert.Equal(first, second);
        Assert.EndsWith("\n", source, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', source);
        Assert.Contains(
            "export const contractKey = \"netwasm:worker/wit@1.0.0\";",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "const binding0 = root[\"mathAlias\"];",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "binding0[\"addValue\"](...args);",
            source,
            StringComparison.Ordinal);
        Assert.Contains("const binding1 = root;", source, StringComparison.Ordinal);
        Assert.Contains(
            "binding1[\"rootValue\"](...args);",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "root[\"netwasm:runtime/reactor-guest@1.0.0\"]",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("split(", source, StringComparison.Ordinal);
        Assert.Contains("const guestWake = reactorGuest[\"wake\"].bind(reactorGuest);",
            source, StringComparison.Ordinal);
        Assert.Contains("componentImports = bindReactorHost(imports, reactorHost)",
            source, StringComparison.Ordinal);
        Assert.Contains("[\"netwasm:runtime/reactor-host\"]: binding",
            source, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolvesQualifiedInterfaceFromPinnedJcoRootMetadata()
    {
        var source = Encoding.UTF8.GetString(_subject.Write(new(
            Encoding.UTF8.GetBytes(Contract(Export(
                "example:worker/math-api@1.0.0/add-value",
                "interface",
                "interface-0",
                "example:worker/math-api@1.0.0",
                "interface0",
                "addValue",
                "add-value",
                [],
                Type("s32")))),
            JcoVersion,
            [
                new("interface0", "instance"),
                new("mathApi", "instance"),
                new("example:worker/math-api@1.0.0", "instance"),
            ])));

        Assert.Contains(
            "const binding0 = root[\"example:worker/math-api@1.0.0\"];",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void OmitsReactorBindingWhenPackagedWorldDoesNotDeclareIt()
    {
        var source = Encoding.UTF8.GetString(_subject.Write(new(
            Encoding.UTF8.GetBytes(Contract(Export(
                "value", "root", "value", null, "", "value", "value", [], null))),
            JcoVersion,
            [new("value", "function")])));

        Assert.Contains("const guestWake = null;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("reactorGuest", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsNullRequestUnsupportedJcoAndMissingContract()
    {
        Assert.Throws<ArgumentNullException>(() => _subject.Write(null!));
        Assert.Throws<NotSupportedException>(() => _subject.Write(new(
            "{}"u8.ToArray(),
            "1.28.0",
            [])));
        Assert.Throws<ArgumentException>(() => _subject.Write(new(
            ReadOnlyMemory<byte>.Empty,
            JcoVersion,
            [])));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "function")]
    [InlineData("value", "other")]
    public void RejectsInvalidPinnedJcoRootMetadata(string? name, string? kind)
    {
        var exports = name is null && kind is null
            ? default
            : System.Collections.Immutable.ImmutableArray.Create(
                new WitWorkerRootExport(name!, kind!));
        var contract = Contract(Export(
            "value", "root", "value", null, "", "value", "value", [], null));

        Assert.Throws<InvalidDataException>(() => _subject.Write(new(
            Encoding.UTF8.GetBytes(contract),
            JcoVersion,
            exports)));
    }

    [Fact]
    public void RejectsPinnedJcoMetadataThatDoesNotMatchTheContract()
    {
        var root = Contract(Export(
            "value", "root", "value", null, "", "value", "value", [], null));
        Assert.Throws<InvalidDataException>(() => Write(root, []));
        Assert.Throws<InvalidDataException>(() => Write(
            root, [new("value", "instance")]));

        var instance = Contract(Export(
            "example:worker/math-api@1.0.0/add-value",
            "interface",
            "math-alias",
            "example:worker/math-api@1.0.0",
            "mathAlias",
            "addValue",
            "add-value",
            [],
            null));
        Assert.Throws<InvalidDataException>(() => Write(
            instance, [new("mathAlias", "function")]));
        Assert.Throws<InvalidDataException>(() => Write(
            instance, [new("example:worker/math-api@1.0.0", "function")]));
        Assert.Throws<InvalidDataException>(() => Write(instance, []));

        Assert.Throws<InvalidDataException>(() => Write(
            root,
            [new("value", "function"), new("value", "function")]));
        Assert.Throws<InvalidDataException>(() => Write(root, [null!]));
    }

    private byte[] Write(
        string contract,
        System.Collections.Immutable.ImmutableArray<WitWorkerRootExport> exports) =>
        _subject.Write(new(Encoding.UTF8.GetBytes(contract), JcoVersion, exports));

    [Theory]
    [MemberData(nameof(InvalidContracts))]
    public void RejectsInvalidContractShapes(string json, Type exceptionType)
    {
        var exception = Record.Exception(() => _subject.Write(new(
            Encoding.UTF8.GetBytes(json),
            JcoVersion,
            [new("value", "function")])));
        Assert.NotNull(exception);
        Assert.IsType(exceptionType, exception);
    }

    public static TheoryData<string, Type> InvalidContracts()
    {
        var data = new TheoryData<string, Type>
        {
            { "{", typeof(InvalidDataException) },
            { "null", typeof(InvalidDataException) },
            { "{\"schemaVersion\":1,\"world\":\"a:b/w@1.0.0\",\"exports\":[],\"types\":[],\"extra\":true}", typeof(InvalidDataException) },
        };
        foreach (var mutation in Mutations())
        {
            data.Add(mutation.Json, mutation.ExceptionType);
        }
        return data;
    }

    private static IEnumerable<(string Json, Type ExceptionType)> Mutations()
    {
        var valid = JsonNode.Parse(Contract(Export(
            "value",
            "root",
            "value",
            null,
            "",
            "value",
            "value",
            [Parameter("input", Type("s32"))],
            Type("s32"))))!.AsObject();

        yield return Mutate(valid, root => root["schemaVersion"] = 3);
        yield return Mutate(valid, root => root["world"] = " ");
        yield return Mutate(valid, root => root["exports"] = new JsonArray());
        yield return Mutate(valid, root => root.Remove("types"));
        yield return Mutate(valid, root => root["exports"]![0] = null);
        yield return Mutate(valid, root => ExportNode(root)["operation"] = "");
        yield return Mutate(valid, root => ExportNode(root)["operation"] = "__proto__");
        yield return Mutate(valid, root =>
        {
            var duplicate = ExportNode(root).DeepClone();
            root["exports"]!.AsArray().Add(duplicate);
        });
        yield return Mutate(valid, root => ExportNode(root)["worldItem"] = "");
        yield return Mutate(valid, root => ExportNode(root)["function"] = "");
        yield return Mutate(valid, root => ExportNode(root)["javaScriptMember"] = "");
        yield return Mutate(valid, root => ExportNode(root).Remove("parameters"));
        yield return Mutate(valid, root => ExportNode(root)["kind"] = null);
        yield return Mutate(valid, root => ExportNode(root)["kind"]!["name"] = "method");
        yield return Mutate(valid, root => ExportNode(root)["kind"]!["resourceType"] = 0);
        yield return Mutate(valid, root => ExportNode(root)["interface"] = "unexpected");
        yield return Mutate(valid, root => ExportNode(root)["javaScriptRoot"] = "unexpected");
        yield return Mutate(valid, root => ExportNode(root)["javaScriptRoot"] = null);
        yield return Mutate(valid, root => ExportNode(root)["placement"] = "other");
        yield return Mutate(valid, root => ExportNode(root)["parameters"]![0] = null);
        yield return Mutate(valid, root => ExportNode(root)["parameters"]![0]!["name"] = "");
        yield return Mutate(valid, root =>
        {
            var duplicate = ExportNode(root)["parameters"]![0]!.DeepClone();
            ExportNode(root)["parameters"]!.AsArray().Add(duplicate);
        });
        yield return Mutate(valid, root => ExportNode(root)["parameters"]![0]!["type"] = null,
            typeof(InvalidDataException));
        yield return Mutate(valid, root => ExportNode(root)["parameters"]![0]!["type"]!["kind"] = "defined",
            typeof(InvalidDataException));
        yield return Mutate(valid, root => ExportNode(root)["parameters"]![0]!["type"]!["definition"] = 0,
            typeof(InvalidDataException));
        yield return Mutate(valid, root => ExportNode(root)["parameters"]![0]!["type"]!["primitive"] = "record",
            typeof(InvalidDataException));
        yield return Mutate(valid, root => ExportNode(root)["result"]!["primitive"] = null,
            typeof(InvalidDataException));
        yield return Mutate(valid, root =>
        {
            var item = ExportNode(root);
            item["placement"] = "interface";
            item["interface"] = null;
            item["javaScriptRoot"] = "";
        });
        yield return Mutate(valid, root =>
        {
            var item = ExportNode(root);
            item["placement"] = "interface";
            item["interface"] = "example:worker/api@1.0.0";
            item["javaScriptRoot"] = "";
        });
        yield return Mutate(valid, root => root["reactor"] = new JsonObject());
        yield return Mutate(valid, root =>
        {
            root["reactor"] = Reactor();
            root["reactor"]!["interface"] = "example:worker/reactor@1.0.0";
        });
        yield return Mutate(valid, root =>
        {
            root["reactor"] = Reactor();
            root["reactor"]!["javaScriptRoot"] = "";
        });
        yield return Mutate(valid, root =>
        {
            root["reactor"] = Reactor();
            root["reactor"]!["javaScriptMember"] = "";
        });
    }

    private static (string Json, Type ExceptionType) Mutate(
        JsonObject valid,
        Action<JsonObject> mutation,
        Type? exceptionType = null)
    {
        var copy = valid.DeepClone().AsObject();
        mutation(copy);
        return (copy.ToJsonString(), exceptionType ?? typeof(InvalidDataException));
    }

    private static JsonObject ExportNode(JsonObject root) =>
        root["exports"]![0]!.AsObject();

    private static string Contract(params JsonObject[] exports) => new JsonObject
    {
        ["schemaVersion"] = 2,
        ["world"] = "example:worker/application@1.0.0",
        ["exports"] = new JsonArray(exports.Select(export => export.DeepClone()).ToArray()),
        ["types"] = new JsonArray(),
        ["reactor"] = null,
    }.ToJsonString();

    private static JsonObject Reactor() => new()
    {
        ["interface"] = "netwasm:runtime/reactor-guest@1.0.0",
        ["javaScriptRoot"] = "netwasm:runtime/reactor-guest@1.0.0",
        ["javaScriptMember"] = "wake",
    };

    private static JsonObject Export(
        string operation,
        string placement,
        string worldItem,
        string? interfaceName,
        string javaScriptRoot,
        string javaScriptMember,
        string function,
        JsonObject[] parameters,
        JsonObject? result) => new()
        {
            ["operation"] = operation,
            ["placement"] = placement,
            ["worldItem"] = worldItem,
            ["interface"] = interfaceName,
            ["javaScriptRoot"] = javaScriptRoot,
            ["javaScriptMember"] = javaScriptMember,
            ["function"] = function,
            ["parameters"] = new JsonArray(parameters.Select(parameter => parameter.DeepClone()).ToArray()),
            ["result"] = result?.DeepClone(),
            ["kind"] = new JsonObject
            {
                ["name"] = "freestanding",
                ["resourceType"] = null,
            },
        };

    private static JsonObject[] AllPrimitiveParameters() =>
        PrimitiveTypes.Select(
            (type, index) => Parameter($"p{index}", Type(type))).ToArray();

    private static JsonObject Parameter(string name, JsonObject type) => new()
    {
        ["name"] = name,
        ["type"] = type.DeepClone(),
    };

    private static JsonObject Type(string primitive) => new()
    {
        ["kind"] = "primitive",
        ["primitive"] = primitive,
        ["definition"] = null,
    };
}
