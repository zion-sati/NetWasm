using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NetWasm.Hosting.Build.JavaScript;
using NetWasm.Hosting.Deployment;

namespace NetWasm.Hosting.Build.Tests;

public sealed class WitWorkerClientWriterTests
{
    [Fact]
    public void WritesTypedRemoteCallsAndCopiesBeforeSubmission()
    {
        var request = Request();
        var writer = WitWorkerClientComposition.CreateWriter();
        var first = writer.Write(request); var second = writer.Write(request);
        Assert.Equal(first.JavaScript, second.JavaScript); Assert.Equal(first.Declaration, second.Declaration);
        var js = Encoding.UTF8.GetString(first.JavaScript); var ts = Encoding.UTF8.GetString(first.Declaration);
        Assert.Contains("const args = [copyType42(argument0)]; return session.invoke(\"echo\", args, options)", js, StringComparison.Ordinal);
        Assert.Contains("const fields = objectData(value); return", js, StringComparison.Ordinal);
        Assert.Contains("\"echo\"(argument0: Wititem_", ts, StringComparison.Ordinal);
        Assert.Contains("Promise<Wititem_", ts, StringComparison.Ordinal);
        Assert.Contains("signal?: AbortSignal", ts, StringComparison.Ordinal);
        Assert.Contains("timeoutMilliseconds?: number", ts, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonSerializer", js, StringComparison.Ordinal);
        Assert.EndsWith("\n", js, StringComparison.Ordinal); Assert.DoesNotContain('\r', js);
        Assert.Equal(1, js.Split("const session =", StringSplitOptions.None).Length - 1);
        Assert.True(
            js.IndexOf("worker notification handler must be a function", StringComparison.Ordinal) <
            js.IndexOf("new Worker", StringComparison.Ordinal));
        Assert.Contains("worker, onNotification,", js, StringComparison.Ordinal);
    }

    [Fact]
    public void WritesVoidAndZeroArgumentOperations()
    {
        var json = JsonNode.Parse(Request().ContractJson.Span)!.AsObject();
        json["exports"]![0]!["parameters"] = new JsonArray(); json["exports"]![0]!["result"] = null;
        var source = WitWorkerClientComposition.CreateWriter().Write(Request() with { ContractJson = Encoding.UTF8.GetBytes(json.ToJsonString()) });
        Assert.Contains("[\"echo\"]: (options)", Encoding.UTF8.GetString(source.JavaScript), StringComparison.Ordinal);
        Assert.Contains("Promise<void>", Encoding.UTF8.GetString(source.Declaration), StringComparison.Ordinal);
    }

    [Fact]
    public void RequiresExplicitDependenciesAndPublicationIdentity()
    {
        var reader = WitWorkerContractComposition.CreateReader(); var planner = new WitWorkerValueLayoutPlanner(); var writers = WitWorkerValueSourceComposition.CreateWriters();
        Assert.Throws<ArgumentNullException>(() => new WitWorkerClientWriter(null!, planner, writers));
        Assert.Throws<ArgumentNullException>(() => new WitWorkerClientWriter(reader, null!, writers));
        Assert.Throws<ArgumentNullException>(() => new WitWorkerClientWriter(reader, planner, null!));
        var writer = WitWorkerClientComposition.CreateWriter(); var request = Request();
        Assert.Throws<ArgumentNullException>(() => writer.Write(null!));
        foreach (var invalid in new[] { request with { HostingJavaScriptRoot = "relative" }, request with { WorkerFileName = "" },
            request with { WorkerFileName = "../worker.mjs" }, request with { WorkerFileName = "folder\\worker.mjs" },
            request with { BuildFingerprint = "a" }, request with { BuildFingerprint = new string('G', 64) }, request with { ManifestSha256 = new string('g', 64) },
            request with { BuildFingerprint = new string('0', 64), ManifestSha256 = "short" } })
            Assert.Throws<ArgumentException>(() => writer.Write(invalid));
        Assert.Throws<NotSupportedException>(() => new WitWorkerClientWriter(reader, planner,
            System.Collections.Immutable.ImmutableDictionary<string, IWitWorkerValueSourceWriter>.Empty).Write(request));
        var json = JsonNode.Parse(request.ContractJson.Span)!.AsObject(); json["types"]![0]!["kind"] = "resource";
        json["exports"]![0]!["kind"] = JsonNode.Parse("{\"name\":\"constructor\",\"resourceType\":42}");
        Assert.Throws<NotSupportedException>(() => writer.Write(request with { ContractJson = Encoding.UTF8.GetBytes(json.ToJsonString()) }));
    }

    internal static WitWorkerClientRequest Request() => new(Encoding.UTF8.GetBytes("""
        {"schemaVersion":2,"world":"example:worker/application@1.0.0","exports":[{"operation":"echo","placement":"root","worldItem":"echo","interface":null,
        "javaScriptRoot":"","javaScriptMember":"echo","function":"echo","parameters":[{"name":"value","type":{"kind":"defined","primitive":null,"definition":42}}],
        "result":{"kind":"defined","primitive":null,"definition":42},"kind":{"name":"freestanding","resourceType":null}}],
        "types":[{"id":42,"name":"item","owner":{"kind":"interface","identity":"example:worker/values@1.0.0"},"kind":{"record":{"fields":[{"name":"text","type":"string"}]}}}],"reactor":null}
        """), Path.GetFullPath("hosting"), "worker.mjs", new string('a', 64), new string('b', 64));
}

internal static class WitWorkerValueSourceFixture
{
    internal static WitWorkerValueSourceRequest Request(string kind, string value, bool nullable = false) =>
        new(1, new("Sample", kind, JsonDocument.Parse(value).RootElement.Clone(), false, null),
            System.Collections.Immutable.ImmutableDictionary<int, WitWorkerValueLayout>.Empty
                .Add(42, new("Referenced", "type", default, nullable, "Int8Array")));
}

public sealed class WitWorkerUnaryValueSourceWriterTests
{
    [Theory]
    [InlineData("bool", "boolean")]
    [InlineData("char", "string")]
    [InlineData("s64", "bigint")]
    [InlineData("u64", "bigint")]
    [InlineData("f64", "number")]
    public void MapsEachPrimitiveTypeFamily(string primitive, string type)
    {
        var request = WitWorkerValueSourceFixture.Request("type", "\"string\"");
        Assert.Equal(type, request.Type(new WitWorkerValueReference("primitive", primitive, null)));
    }
    [Theory]
    [InlineData("type", "\"string\"", "copystring(value)", "string")]
    [InlineData("type", "42", "copyType42(value)", "Referenced")]
    [InlineData("list", "\"string\"", "arrayData(value).map", "Array<string>")]
    [InlineData("list", "\"u8\"", "typedData", "Uint8Array")]
    [InlineData("list", "42", "typedData", "Int8Array")]
    [InlineData("option", "\"u8\"", "value === undefined", "number | undefined")]
    public void EmitsReferenceBoundCopies(string kind, string value, string copy, string type)
    {
        var source = new WitWorkerUnaryValueSourceWriter(kind).Write(WitWorkerValueSourceFixture.Request(kind, value));
        Assert.Contains(copy, source.JavaScript, StringComparison.Ordinal); Assert.Equal(type, source.TypeScript);
    }

    [Fact]
    public void DistinguishesSomeNoneFromNone()
    {
        var source = new WitWorkerUnaryValueSourceWriter("option").Write(WitWorkerValueSourceFixture.Request("option", "42", true));
        Assert.Contains("tag === 'none'", source.JavaScript, StringComparison.Ordinal);
        Assert.Contains("tag === 'some'", source.JavaScript, StringComparison.Ordinal);
        Assert.Contains("property(fields, 'val', true)", source.JavaScript, StringComparison.Ordinal);
        Assert.Contains("val: Referenced", source.TypeScript, StringComparison.Ordinal);
    }
}

public sealed class WitWorkerRecordValueSourceWriterTests
{
    [Fact]
    public void CopiesDataPropertiesWithSafeNamesAndOptionalFields()
    {
        var request = WitWorkerValueSourceFixture.Request("record", "{\"fields\":[{\"name\":\"URL-value\",\"type\":\"string\"},{\"name\":\"maybe\",\"type\":42}]}", true);
        var source = new WitWorkerRecordValueSourceWriter().Write(request);
        Assert.Contains("[\"urlValue\"]: copystring(property(fields, \"urlValue\", false))", source.JavaScript, StringComparison.Ordinal);
        Assert.Contains("\"maybe\"?: Referenced", source.TypeScript, StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => new WitWorkerRecordValueSourceWriter().Write(
            WitWorkerValueSourceFixture.Request("record", "{\"fields\":[{\"name\":\"URL\",\"type\":\"u8\"},{\"name\":\"url\",\"type\":\"u8\"}]}")));
    }
}

public sealed class WitWorkerTupleValueSourceWriterTests
{
    [Fact]
    public void RequiresExactArityAndKeepsElementOrder()
    {
        var source = new WitWorkerTupleValueSourceWriter().Write(WitWorkerValueSourceFixture.Request("tuple", "{\"types\":[\"u32\",42]}"));
        Assert.Equal("[number, Referenced]", source.TypeScript);
        Assert.Contains("arrayData(value, 2)", source.JavaScript, StringComparison.Ordinal);
        Assert.Contains("[copyu32(values[0]), copyType42(values[1])]", source.JavaScript, StringComparison.Ordinal);
    }
}

public sealed class WitWorkerTaggedValueSourceWriterTests
{
    [Theory]
    [InlineData("variant", "{\"cases\":[{\"name\":\"empty\",\"type\":null},{\"name\":\"value\",\"type\":\"u32\"}]}\n", "\"empty\"")]
    [InlineData("result", "{\"ok\":null,\"err\":\"string\"}", "\"ok\", val: void")]
    [InlineData("result", "{\"ok\":\"u32\",\"err\":null}", "\"err\", val: void")]
    public void PreservesTagsAndAbsentPayloads(string kind, string value, string declaration)
    {
        var source = new WitWorkerTaggedValueSourceWriter(kind).Write(WitWorkerValueSourceFixture.Request(kind, value));
        Assert.Contains(declaration, source.TypeScript, StringComparison.Ordinal);
        Assert.Contains("default: throw invalidValue()", source.JavaScript, StringComparison.Ordinal);
        Assert.Contains("switch (tag)", source.JavaScript, StringComparison.Ordinal);
    }
}

public sealed class WitWorkerEnumValueSourceWriterTests
{
    [Fact]
    public void PreservesWitEnumNames()
    {
        var source = new WitWorkerEnumValueSourceWriter().Write(WitWorkerValueSourceFixture.Request("enum", "{\"cases\":[{\"name\":\"first-value\"},{\"name\":\"second\"}]}"));
        Assert.Equal("\"first-value\" | \"second\"", source.TypeScript);
        Assert.Contains("includes(value)", source.JavaScript, StringComparison.Ordinal);
    }
}

public sealed class WitWorkerFlagsValueSourceWriterTests
{
    [Fact]
    public void CopiesOptionalBooleansAndRejectsCollisions()
    {
        var source = new WitWorkerFlagsValueSourceWriter().Write(WitWorkerValueSourceFixture.Request("flags", "{\"flags\":[{\"name\":\"HTTP-status\"}]}"));
        Assert.Equal("{ \"httpStatus\"?: boolean }", source.TypeScript);
        Assert.Contains("optionalBool(property(fields, \"httpStatus\", true))", source.JavaScript, StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => new WitWorkerFlagsValueSourceWriter().Write(
            WitWorkerValueSourceFixture.Request("flags", "{\"flags\":[{\"name\":\"URL\"},{\"name\":\"url\"}]}")));
    }
}
