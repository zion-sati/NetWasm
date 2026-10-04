using System.Text;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using NetWasm.Hosting.Deployment;
using NetWasm.Wit.Bindings.Workers;

namespace NetWasm.Wit.Bindings.Tests;

public sealed class WitWorkerClientOptionsReaderTests
{
    [Fact]
    public void ReadsAllOptionsWithoutSelectingExternalTools()
    {
        var reader = new WitWorkerClientOptionsReader();
        var result = reader.Read(Arguments());
        Assert.Equal("contract.json", result.ContractPath);
        Assert.Equal("Client.cs", result.OutputPath);
        Assert.Equal("Client.mjs", result.JavaScriptOutputPath);
        Assert.Equal("Example.Workers", result.Namespace);
        Assert.Equal("Client", result.ClassName);
        Assert.Equal("example.worker", result.JavaScriptModule);
        Assert.Equal("./worker/client.mjs", result.WorkerClientModule);
    }

    [Fact]
    public void RejectsMissingDuplicateUnknownAndBlankOptions()
    {
        var reader = new WitWorkerClientOptionsReader();
        Assert.Throws<ArgumentNullException>(() => reader.Read(null!));
        foreach (var arguments in new[] { Array.Empty<string>(), Arguments()[..^1], Arguments()[..^2],
            new[] { "unknown", "value" }, new[] { "--class", " " },
            new[] { "--class", "Client", "--class", "Other" } })
            Assert.Throws<WitBindingException>(() => reader.Read(arguments));
    }

    private static string[] Arguments() => ["--worker-contract", "contract.json", "--output", "Client.cs",
        "--javascript-output", "Client.mjs", "--namespace", "Example.Workers", "--class", "Client",
        "--javascript-module", "example.worker", "--worker-client-module", "./worker/client.mjs"];
}

public sealed class WitWorkerCSharpContractProjectorTests
{
    [Fact]
    public void PreservesSemanticOwnersAndRemapsSparseNestedReferences()
    {
        var input = WitWorkerClientGenerationFixture.Contract(
            (42, "item", "{\"record\":{\"fields\":[{\"name\":\"value\",\"type\":\"u32\"}]}}"),
            (99, "items", "{\"list\":42}"),
            (105, "pair", "{\"tuple\":{\"types\":[42,99]}}"));
        var contract = WitWorkerContractComposition.CreateReader().Read(input);
        var projector = new WitWorkerCSharpContractProjector(new WitWorkerValueLayoutPlanner());
        var result = projector.Project(contract);
        Assert.Collection(result.Document.Types, type => Assert.Equal(0, type.Id),
            type => Assert.Equal(1, type.Id), type => Assert.Equal(2, type.Id));
        Assert.Equal(0, result.Document.Types[1].Kind.GetProperty("list").GetInt32());
        Assert.Equal(1, result.Document.Types[2].Kind.GetProperty("tuple").GetProperty("types")[1].GetInt32());
        Assert.Equal(new WitTypeReference.Defined(1), result.Reference(new("defined", null, 99)));
        Assert.Equal(new WitTypeReference.Primitive("u32"), result.Reference(new("primitive", "u32", null)));
        Assert.Equal(result.Layouts[42].Name, result.Document.Types[0].Name);
    }

    [Fact]
    public void RejectsResourceBearingWorkerContracts()
    {
        Assert.Throws<ArgumentNullException>(() => new WitWorkerCSharpContractProjector(null!));
        var projector = new WitWorkerCSharpContractProjector(new WitWorkerValueLayoutPlanner());
        Assert.Throws<ArgumentNullException>(() => projector.Project(null!));
        var input = WitWorkerClientGenerationFixture.Contract((42, "resource", "\"resource\""));
        var contract = WitWorkerContractComposition.CreateReader().Read(input);
        Assert.Throws<NotSupportedException>(() => projector.Project(contract));
    }
}

public sealed class WitWorkerCSharpClientWriterTests
{
    [Fact]
    public void EmitsDeterministicClientsWithExplicitTerminationAndCallCancellation()
    {
        var writer = WitWorkerCSharpClientComposition.CreateWriter();
        var request = WitWorkerClientGenerationFixture.Request();
        var first = writer.Write(request);
        var second = writer.Write(request);
        Assert.Equal(first.CSharp, second.CSharp);
        Assert.Equal(first.JavaScript, second.JavaScript);
        var cs = Encoding.UTF8.GetString(first.CSharp);
        var js = Encoding.UTF8.GetString(first.JavaScript);
        Assert.Contains("Task<uint> EchoAsync(CancellationToken cancellationToken = default)", cs, StringComparison.Ordinal);
        Assert.Contains("try { Terminate(_session); } finally { _session.Dispose(); }", cs, StringComparison.Ordinal);
        Assert.Contains("try { Cancel(invocation); } finally", cs, StringComparison.Ordinal);
        Assert.Contains("invocation.Dispose();", cs, StringComparison.Ordinal);
        Assert.Contains("...args, { signal }", js, StringComparison.Ordinal);
        Assert.Contains("invocation.controller.abort()", js, StringComparison.Ordinal);
        Assert.Contains("owner.dispose()", js, StringComparison.Ordinal);
        Assert.Contains("new AbortController()", js, StringComparison.Ordinal);
        Assert.DoesNotContain("new Map(", js, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonSerializer", cs, StringComparison.Ordinal);
    }

    [Fact]
    public void ScopesValueDeclarationsInsideEachClient()
    {
        var request = WitWorkerClientGenerationFixture.Request() with
        {
            ContractJson = WitWorkerClientGenerationFixture.Contract(
                (42, "shared", "{\"record\":{\"fields\":[]}}")),
        };
        var writer = WitWorkerCSharpClientComposition.CreateWriter();
        foreach (var name in new[] { "FirstClient", "SecondClient" })
        {
            var cs = Encoding.UTF8.GetString(writer.Write(request with { ClassName = name }).CSharp);
            var client = cs.IndexOf($"public sealed class {name} : IDisposable\n{{", StringComparison.Ordinal);
            var value = cs.IndexOf("public readonly struct ", StringComparison.Ordinal);
            Assert.True(client >= 0 && value > client);
            Assert.DoesNotContain("}\npublic readonly struct ", cs, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void RejectsInvalidIdentifiersAndUnsafeModuleLocations()
    {
        var writer = WitWorkerCSharpClientComposition.CreateWriter();
        var request = WitWorkerClientGenerationFixture.Request();
        Assert.Throws<ArgumentNullException>(() => writer.Write(null!));
        foreach (var invalid in new[] { request with { Namespace = "" }, request with { Namespace = "Example.class" },
            request with { ClassName = "1Client" }, request with { ClassName = "class" }, request with { ClassName = "Client-name" },
            request with { JavaScriptModule = " " }, request with { WorkerClientModule = "../client.mjs" },
            request with { WorkerClientModule = "./../client.mjs" }, request with { WorkerClientModule = "././client.mjs" },
            request with { WorkerClientModule = "./client.mjs?query" }, request with { WorkerClientModule = "./client.mjs#fragment" },
            request with { WorkerClientModule = "./folder\\client.mjs" } })
            Assert.Throws<ArgumentException>(() => writer.Write(invalid));
    }

    [Theory]
    [InlineData("{\"type\":\"u32\"}")]
    [InlineData("{\"list\":\"u8\"}")]
    [InlineData("{\"list\":\"string\"}")]
    [InlineData("{\"option\":\"u32\"}")]
    [InlineData("{\"record\":{\"fields\":[]}}")]
    [InlineData("{\"record\":{\"fields\":[{\"name\":\"HTTP-status\",\"type\":\"u32\"}]}}")]
    [InlineData("{\"tuple\":{\"types\":[]}}")]
    [InlineData("{\"tuple\":{\"types\":[\"u32\"]}}")]
    [InlineData("{\"tuple\":{\"types\":[\"u32\",\"string\"]}}")]
    [InlineData("{\"variant\":{\"cases\":[{\"name\":\"empty\",\"type\":null},{\"name\":\"value\",\"type\":\"u32\"}]}}")]
    [InlineData("{\"result\":{\"ok\":\"u32\",\"err\":null}}")]
    [InlineData("{\"result\":{\"ok\":null,\"err\":\"string\"}}")]
    [InlineData("{\"enum\":{\"cases\":[{\"name\":\"first\"},{\"name\":\"second\"}]}}")]
    [InlineData("{\"flags\":{\"flags\":[{\"name\":\"read\"},{\"name\":\"write\"}]}}")]
    public void EmitsBothSidesOfEveryNonResourceValueShape(string kind)
    {
        var bytes = WitWorkerClientGenerationFixture.Contract((42, "value", kind));
        var source = WitWorkerCSharpClientComposition.CreateWriter().Write(WitWorkerClientGenerationFixture.Request() with { ContractJson = bytes });
        var cs = Encoding.UTF8.GetString(source.CSharp);
        var js = Encoding.UTF8.GetString(source.JavaScript);
        Assert.Contains("WriteType42(", cs, StringComparison.Ordinal);
        Assert.Contains("ReadType42(", cs, StringComparison.Ordinal);
        Assert.Contains("writeType42(", js, StringComparison.Ordinal);
        Assert.Contains("readType42(", js, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsMissingWriterCapabilities()
    {
        var contracts = WitWorkerContractComposition.CreateReader();
        var projector = new WitWorkerCSharpContractProjector(new WitWorkerValueLayoutPlanner());
        var definitions = new TypeDefinitions.WitTypeDefinitionClassifier();
        var syntax = new WitBindingSyntaxFormatter(definitions);
        var declarations = new WitTypeDeclarationWriter(syntax, new CodeWriterFactory(), new NetWasm.Wit.Bindings.Aliases.WitAliasValidator(syntax), definitions);
        var codecs = WitWorkerValueCodecComposition.CreateWriters();
        Assert.Throws<ArgumentNullException>(() => new WitWorkerCSharpClientWriter(null!, projector, declarations, syntax, codecs));
        Assert.Throws<ArgumentNullException>(() => new WitWorkerCSharpClientWriter(contracts, null!, declarations, syntax, codecs));
        Assert.Throws<ArgumentNullException>(() => new WitWorkerCSharpClientWriter(contracts, projector, null!, syntax, codecs));
        Assert.Throws<ArgumentNullException>(() => new WitWorkerCSharpClientWriter(contracts, projector, declarations, null!, codecs));
        Assert.Throws<ArgumentNullException>(() => new WitWorkerCSharpClientWriter(contracts, projector, declarations, syntax, null!));
    }

    [Fact]
    public void WritesArgumentsAndVoidResultsAndDisambiguatesOperationNames()
    {
        var json = JsonNode.Parse(WitWorkerClientGenerationFixture.Contract())!;
        var first = json["exports"]![0]!.DeepClone();
        first["operation"] = "a";
        first["parameters"] = JsonNode.Parse("""[{"name":"input","type":{"kind":"primitive","primitive":"u32","definition":null}}]""");
        first["result"] = null;
        var second = first.DeepClone();
        second["operation"] = "b";
        json["exports"] = new JsonArray(first, second);
        var request = WitWorkerClientGenerationFixture.Request() with { ContractJson = Encoding.UTF8.GetBytes(json.ToJsonString()) };
        var source = WitWorkerCSharpClientComposition.CreateWriter().Write(request);
        var cs = Encoding.UTF8.GetString(source.CSharp);
        Assert.Contains("Task EchoAsync(uint argument0, CancellationToken cancellationToken = default)", cs, StringComparison.Ordinal);
        Assert.Contains("Writeu32(writer, argument0);", cs, StringComparison.Ordinal);
        Assert.Contains("reader.End();", cs, StringComparison.Ordinal);
        Assert.Contains(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("b")))[..12] + "Async", cs, StringComparison.Ordinal);
        Assert.Contains("const args = [readu32(reader)]", Encoding.UTF8.GetString(source.JavaScript), StringComparison.Ordinal);

        var third = first.DeepClone(); third["operation"] = "c";
        json["exports"]!.AsArray().Add(third);
        var definitions = new TypeDefinitions.WitTypeDefinitionClassifier();
        var normalSyntax = new WitBindingSyntaxFormatter(definitions);
        var collidingName = normalSyntax.Format(new WitBindingSyntaxRequest.Identifier("echo")) + "_"
            + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("c")))[..12];
        var collidingSyntax = new CollidingSyntax(normalSyntax, collidingName);
        var writer = new WitWorkerCSharpClientWriter(WitWorkerContractComposition.CreateReader(),
            new WitWorkerCSharpContractProjector(new WitWorkerValueLayoutPlanner()),
            new WitTypeDeclarationWriter(normalSyntax, new CodeWriterFactory(), new NetWasm.Wit.Bindings.Aliases.WitAliasValidator(normalSyntax), definitions),
            collidingSyntax, WitWorkerValueCodecComposition.CreateWriters());
        Assert.Throws<InvalidDataException>(() => writer.Write(request with { ContractJson = Encoding.UTF8.GetBytes(json.ToJsonString()) }));
    }

    [Fact]
    public void PreservesAllThirtyTwoFlagBitsAndFormatsJsonTypeReferences()
    {
        var flags = new JsonArray(Enumerable.Range(0, 32).Select(index => (JsonNode)new JsonObject { ["name"] = "flag-" + index }).ToArray());
        var bytes = WitWorkerClientGenerationFixture.Contract((42, "flags", new JsonObject { ["flags"] = new JsonObject { ["flags"] = flags } }.ToJsonString()));
        var contract = new WitWorkerCSharpContractProjector(new WitWorkerValueLayoutPlanner()).Project(WitWorkerContractComposition.CreateReader().Read(bytes));
        var syntax = new WitBindingSyntaxFormatter(new TypeDefinitions.WitTypeDefinitionClassifier());
        var request = new WitWorkerValueCodecRequest(42, contract, syntax);
        Assert.Equal("uint", request.Type(JsonSerializer.SerializeToElement("u32")));
        var codec = new WitWorkerFlagsValueCodecWriter().Write(request);
        Assert.Contains("bits & 0u", codec.CSharpWrite, StringComparison.Ordinal);
        Assert.Contains("bits & 0u", codec.CSharpRead, StringComparison.Ordinal);
        Assert.Contains("bits |= 1 << 31", codec.JavaScriptWrite, StringComparison.Ordinal);
        Assert.Contains("bits & (1 << 31)", codec.JavaScriptRead, StringComparison.Ordinal);
    }

    private sealed class CollidingSyntax(IWitBindingSyntaxFormatter syntax, string collision) : IWitBindingSyntaxFormatter
    {
        private int _function;
        public string Format(WitBindingSyntaxRequest request) => request is WitBindingSyntaxRequest.FunctionName
            ? ++_function == 1 ? collision : "Echo" : syntax.Format(request);
    }

    [Fact]
    public void EmitsNestedOptionsAndJaggedArraysWithoutExpandingTypes()
    {
        var bytes = WitWorkerClientGenerationFixture.Contract((42, "option", "{\"option\":\"u32\"}"),
            (43, "nested", "{\"option\":42}"), (44, "array", "{\"list\":\"u32\"}"),
            (45, "arrays", "{\"list\":44}"));
        var source = WitWorkerCSharpClientComposition.CreateWriter().Write(WitWorkerClientGenerationFixture.Request() with { ContractJson = bytes });
        Assert.Contains("new uint[count][]", Encoding.UTF8.GetString(source.CSharp), StringComparison.Ordinal);
        Assert.Contains("{ tag: 'some', val:", Encoding.UTF8.GetString(source.JavaScript), StringComparison.Ordinal);
    }
}

internal static class WitWorkerClientGenerationFixture
{
    internal static WitWorkerCSharpClientRequest Request() => new(Contract(), "Example.Workers", "Client", "example.worker", "./worker/client.mjs");

    internal static byte[] Contract(params (int Id, string Name, string Kind)[] definitions)
    {
        var contract = JsonNode.Parse("""
            {"schemaVersion":2,"world":"example:worker/application@1.0.0","exports":[{"operation":"echo","placement":"root","worldItem":"echo","interface":null,
            "javaScriptRoot":"","javaScriptMember":"echo","function":"echo","parameters":[],"result":{"kind":"primitive","primitive":"u32","definition":null},
            "kind":{"name":"freestanding","resourceType":null}}],"types":[],"reactor":null}
            """)!.AsObject();
        foreach (var (id, name, kind) in definitions)
            contract["types"]!.AsArray().Add(new JsonObject
            {
                ["id"] = id,
                ["name"] = name,
                ["kind"] = JsonNode.Parse(kind),
                ["owner"] = new JsonObject { ["kind"] = "interface", ["identity"] = "example:worker/values@1.0.0" }
            });
        return Encoding.UTF8.GetBytes(contract.ToJsonString());
    }
}
