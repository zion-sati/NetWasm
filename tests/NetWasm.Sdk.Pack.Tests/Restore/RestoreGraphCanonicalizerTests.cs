using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using NetWasm.Sdk.Pack.Restore;

namespace NetWasm.Sdk.Pack.Tests.Restore;

public sealed class RestoreGraphCanonicalizerTests
{
    private static readonly Dictionary<string, string> Profiles =
        new Dictionary<string, string> { ["netwasm0.1"] = "NetWasm,Version=v0.1" };

    [Theory]
    [InlineData(null)]
    [InlineData("netwasm0.1")]
    [InlineData("netwasm01")]
    [InlineData("NetWasm,Version=v0.1")]
    public void CanonicalizesBothFrameworkRecordsWithoutChangingAliasesOrDependencies(string? identity)
    {
        var framework = new JsonObject
        {
            ["targetAlias"] = "netwasm0.1",
            ["dependencies"] = new JsonObject { ["Example"] = new JsonObject { ["version"] = "[1.0.0]" } }
        };
        if (identity is not null)
        {
            framework["framework"] = identity;
        }

        var graph = new JsonObject
        {
            ["format"] = 1,
            ["customField"] = "preserved",
            ["projects"] = new JsonObject
            {
                ["App.csproj"] = new JsonObject
                {
                    ["frameworks"] = new JsonObject
                    {
                        ["netwasm0.1"] = framework.DeepClone(),
                        ["net10.0"] = new JsonObject { ["framework"] = "net10.0", ["targetAlias"] = "net10.0" }
                    },
                    ["restore"] = new JsonObject
                    {
                        ["originalTargetFrameworks"] = new JsonArray("netwasm0.1", "net10.0"),
                        ["frameworks"] = new JsonObject { ["netwasm0.1"] = framework.DeepClone() }
                    }
                },
                ["Other.csproj"] = new JsonObject { ["restore"] = new JsonObject() }
            }
        };
        var input = JsonSerializer.SerializeToUtf8Bytes(graph);
        var canonicalizer = new RestoreGraphCanonicalizer();

        var result = JsonNode.Parse(((IRestoreGraphCanonicalizer)canonicalizer).Canonicalize(input, Profiles))!;

        graph["projects"]!["App.csproj"]!["frameworks"]!["netwasm0.1"]!["framework"] = "NetWasm,Version=v0.1";
        graph["projects"]!["App.csproj"]!["restore"]!["frameworks"]!["netwasm0.1"]!["framework"] = "NetWasm,Version=v0.1";
        Assert.True(JsonNode.DeepEquals(graph, result));
        Assert.Equal(identity, JsonNode.Parse(input)!["projects"]!["App.csproj"]!["frameworks"]!["netwasm0.1"]!["framework"]?.GetValue<string>());
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"projects\":{\"App\":null}}")]
    [InlineData("{\"projects\":{\"App\":{}}}")]
    [InlineData("{\"projects\":{\"App\":{\"restore\":{},\"frameworks\":[]}}}")]
    [InlineData("{\"projects\":{\"App\":{\"restore\":{},\"frameworks\":{\"netwasm0.1\":null}}}}")]
    [InlineData("{\"projects\":{\"App\":{\"restore\":{},\"frameworks\":{\"netwasm0.1\":{\"framework\":\"net10.0\"}}}}}")]
    public void RejectsInvalidOrConflictingContracts(string graph)
    {
        var canonicalizer = new RestoreGraphCanonicalizer();

        Assert.Throws<JsonException>(() => ((IRestoreGraphCanonicalizer)canonicalizer).Canonicalize(Encoding.UTF8.GetBytes(graph), Profiles));
    }

    [Fact]
    public void RejectsMissingInputs()
    {
        var canonicalizer = new RestoreGraphCanonicalizer();
        Assert.Throws<ArgumentNullException>(() => ((IRestoreGraphCanonicalizer)canonicalizer).Canonicalize(null!, Profiles));
        Assert.Throws<ArgumentNullException>(() => ((IRestoreGraphCanonicalizer)canonicalizer).Canonicalize([], null!));
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("unsupported")]
    public void RejectsAnInvalidRegisteredCanonicalIdentity(string identity)
    {
        var canonicalizer = new RestoreGraphCanonicalizer();
        var profiles = new Dictionary<string, string> { ["netwasm0.1"] = identity };
        var graph = Encoding.UTF8.GetBytes("""
            {"projects":{"App":{"restore":{},"frameworks":{"netwasm0.1":{}}}}}
            """);

        Assert.Throws<ArgumentException>(() => ((IRestoreGraphCanonicalizer)canonicalizer).Canonicalize(graph, profiles));
    }
}
