using System.Text;
using System.Text.Json.Nodes;
using NetWasm.Hosting.Deployment;

namespace NetWasm.Hosting.Tests.Deployment;

public sealed class WitWorkerContractReaderTests
{
    [Fact]
    public void PreservesSparseIdsAndSemanticOwnersAfterInputLifetimeEnds()
    {
        var fixture = Fixture();
        var result = Read(fixture);
        fixture["types"] = new JsonArray();
        Assert.Equal([42], result.Definitions.Keys);
        Assert.Equal("example:worker/values@1.0.0", result.Definitions[42].Owner!.Identity);
        Assert.Equal("record", result.Definitions[42].Kind.EnumerateObject().Single().Name);
        Assert.Equal("string", Assert.Single(result.References[42]).Primitive);
    }

    [Fact]
    public void AcceptsResourceMetadataWhileValueAdaptersRejectRemoteOwnership()
    {
        var fixture = Fixture();
        fixture["types"]![0]!["kind"] = "resource";
        fixture["exports"]![0]!["kind"] = JsonNode.Parse("{\"name\":\"method\",\"resourceType\":42}");
        var result = Read(fixture);
        Assert.Equal("method", result.Exports[0]!.Kind!.Name);
        Assert.Empty(result.References[42]);
        var adapter = new WitWorkerComponentAdapterWriter(WitWorkerContractComposition.CreateReader(), new WitWorkerValueLayoutPlanner());
        Assert.Throws<NotSupportedException>(() => adapter.Write(new(Bytes(fixture), CanonicalComponentAdapterWriter.SupportedJcoVersion, [new("echo", "function")])));
    }

    [Theory]
    [MemberData(nameof(InvalidDefinitions))]
    public void RejectsBrokenTypeClosure(string types)
    {
        var fixture = Fixture(); fixture["types"] = JsonNode.Parse(types);
        Assert.Throws<InvalidDataException>(() => Read(fixture));
    }

    public static TheoryData<string> InvalidDefinitions => new()
    {
        "[null]", "[{\"id\":-1,\"kind\":{\"type\":\"u8\"}}]",
        "[{\"id\":42,\"kind\":{\"type\":42}}]",
        "[{\"id\":42,\"kind\":{\"type\":43}}]",
        "[{\"id\":42,\"kind\":{\"type\":\"unknown\"}}]",
        "[{\"id\":42,\"kind\":{\"handle\":{\"own\":43}}},{\"id\":43,\"kind\":{\"type\":\"u8\"}}]",
        "[{\"id\":42,\"name\":\"x\",\"kind\":{\"type\":\"u8\"}}]",
        "[{\"id\":42,\"name\":\" \",\"kind\":{\"type\":\"u8\"},\"owner\":{\"kind\":\"interface\",\"identity\":\"a:b/c\"}}]",
        "[{\"id\":42,\"kind\":{\"type\":\"u8\"},\"owner\":{\"kind\":\"world\",\"identity\":\"a:b/c\"}}]",
        "[{\"id\":42,\"kind\":{\"type\":\"u8\"},\"owner\":{\"kind\":\"interface\",\"identity\":\" \"}}]",
        "[{\"id\":42,\"kind\":{\"type\":\"u8\"}},{\"id\":42,\"kind\":{\"type\":\"u8\"}}]",
        "[{\"id\":42,\"name\":\"x\",\"kind\":{\"type\":\"u8\"},\"owner\":{\"kind\":\"interface\",\"identity\":\"a:b/c\"}},{\"id\":43,\"name\":\"x\",\"kind\":{\"type\":\"u8\"},\"owner\":{\"kind\":\"interface\",\"identity\":\"a:b/c\"}}]",
    };

    [Fact]
    public void RejectsDuplicateJsonKeysAndIncompleteReferences()
    {
        var reader = WitWorkerContractComposition.CreateReader();
        Assert.Throws<InvalidDataException>(() => reader.Read(Encoding.UTF8.GetBytes("{\"world\":\"first\",\"world\":\"second\"}")));
        Assert.Throws<InvalidDataException>(() => reader.Read(ReadOnlyMemory<byte>.Empty));
        Assert.Throws<ArgumentNullException>(() => new WitWorkerContractReader(null!));
        var fixture = Fixture(); fixture["exports"]![0]!["result"] = new JsonObject { ["kind"] = "defined" };
        Assert.Throws<InvalidDataException>(() => Read(fixture));
    }

    [Fact]
    public void ResolvesHandleThroughResourceAliases()
    {
        var fixture = Fixture();
        fixture["types"] = JsonNode.Parse("[{\"id\":42,\"kind\":{\"handle\":{\"borrow\":43}}},{\"id\":43,\"kind\":{\"type\":44}},{\"id\":44,\"kind\":\"resource\"},{\"id\":45,\"kind\":{\"handle\":{\"own\":43}}}]");
        Assert.Equal(4, Read(fixture).Definitions.Count);
    }

    [Theory]
    [InlineData("constructor")]
    [InlineData("static")]
    public void RecognizesEachResourceFunctionKind(string kind)
    {
        var fixture = Fixture(); fixture["types"]![0]!["kind"] = "resource";
        fixture["exports"]![0]!["kind"] = new JsonObject { ["name"] = kind, ["resourceType"] = 42 };
        Assert.Equal(kind, Read(fixture).Exports[0]!.Kind!.Name);
    }

    internal static JsonObject Fixture() => JsonNode.Parse("""
        { "schemaVersion": 2, "world": "example:worker/application@1.0.0",
          "exports": [{ "operation": "echo", "placement": "root", "worldItem": "echo", "interface": null,
            "javaScriptRoot": "", "javaScriptMember": "echo", "function": "echo", "parameters": [],
            "result": { "kind": "defined", "primitive": null, "definition": 42 }, "kind": { "name": "freestanding", "resourceType": null } }],
          "types": [{ "id": 42, "name": "item", "owner": { "kind": "interface", "identity": "example:worker/values@1.0.0" },
            "kind": { "record": { "fields": [{ "name": "text", "type": "string" }] } } }], "reactor": null }
        """)!.AsObject();

    internal static byte[] Bytes(JsonObject value) => Encoding.UTF8.GetBytes(value.ToJsonString());
    internal static WitWorkerResolvedContract Read(JsonObject value) => WitWorkerContractComposition.CreateReader().Read(Bytes(value));
}
