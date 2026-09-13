using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NetWasm.Hosting.Execution;

namespace NetWasm.Hosting.Tests.Execution;

public sealed class NetWasmExecutionRequestReaderTests
{
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void ReadsEitherNewlineAndValidatesDecodedValues(string newline)
    {
        var original = ExecutionRequestFixture.Create();
        var writer = new NetWasmExecutionRequestWriter(new NetWasmExecutionRequestValidator());
        var text = Encoding.UTF8.GetString(writer.Write(original)).Replace("\n", newline, StringComparison.Ordinal);
        var calls = 0;
        var subject = Assert.IsAssignableFrom<INetWasmExecutionRequestReader>(
            new NetWasmExecutionRequestReader(new ExecutionRequestValidationStub(value =>
            {
                Assert.Equal(original.BuildFingerprint, value.BuildFingerprint);
                Assert.Equal<string>(original.Arguments, value.Arguments);
                Assert.Equal<NetWasmEnvironmentVariable>(original.Environment.OrderBy(item => item.Name), value.Environment);
                Assert.Equal(original.Grants.Network, value.Grants.Network);
                Assert.Equal<NetWasmApplicationImport>(original.ApplicationImports.OrderBy(item => item.Module), value.ApplicationImports);
                calls++;
            })));
        var result = subject.Read(Encoding.UTF8.GetBytes(text));
        Assert.Equal(original.DeploymentManifestSha256, result.DeploymentManifestSha256);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":1,\"schemaVersion\":2}")]
    [InlineData("{\"schemaVersion\":1,\"grants\":99}")]
    [InlineData("{\"schemaVersion\":1,\"grants\":\"future\"}")]
    public void RejectsInvalidJsonBeforeValidation(string json)
    {
        var subject = new NetWasmExecutionRequestReader(
            new ExecutionRequestValidationStub(_ => Assert.Fail("Must not validate malformed JSON.")));
        Assert.Throws<JsonException>(() => subject.Read(Encoding.UTF8.GetBytes(json)));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("missing")]
    [InlineData("null")]
    [InlineData("case")]
    [InlineData("duplicate")]
    [InlineData("nested-unknown")]
    [InlineData("nested-missing")]
    [InlineData("nested-null")]
    [InlineData("nested-duplicate")]
    [InlineData("numeric-enum")]
    [InlineData("unknown-enum")]
    public void RejectsMalformedMembersBeforeValidation(string mutation)
    {
        var document = JsonNode.Parse(
            new NetWasmExecutionRequestWriter(new NetWasmExecutionRequestValidator()).Write(ExecutionRequestFixture.Create()))!
            .AsObject();
        var json = mutation switch
        {
            "duplicate" => document.ToJsonString().Insert(1, "\"schemaVersion\":1,"),
            "nested-duplicate" => document.ToJsonString().Replace(
                "\"hostPath\":", "\"hostPath\":\"duplicate\",\"hostPath\":", StringComparison.Ordinal),
            _ => Mutate(document, mutation),
        };
        var subject = new NetWasmExecutionRequestReader(
            new ExecutionRequestValidationStub(_ => Assert.Fail("Must not validate malformed JSON.")));
        Assert.Throws<JsonException>(() => subject.Read(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void PropagatesSemanticValidationFailure()
    {
        var bytes = new NetWasmExecutionRequestWriter(new NetWasmExecutionRequestValidator())
            .Write(ExecutionRequestFixture.Create());
        var failure = new ArgumentException("Rejected request.");
        var subject = new NetWasmExecutionRequestReader(new ExecutionRequestValidationStub(_ => throw failure));
        Assert.Same(failure, Assert.Throws<ArgumentException>(() => subject.Read(bytes)));
    }

    [Fact]
    public void RejectsMissingValidator() =>
        Assert.Throws<ArgumentNullException>(() => new NetWasmExecutionRequestReader(null!));

    private static string Mutate(JsonObject document, string mutation)
    {
        switch (mutation)
        {
            case "unknown": document["workingDirectory"] = "/work"; break;
            case "missing": document.Remove("deploymentManifestSha256"); break;
            case "null": document["deploymentManifestSha256"] = null; break;
            case "case": document["Arguments"] = document["arguments"]!.DeepClone(); document.Remove("arguments"); break;
            case "nested-unknown": document["grants"]!["threads"] = true; break;
            case "nested-missing": document["applicationImports"]![0]!.AsObject().Remove("sha256"); break;
            case "nested-null": document["environment"]![0]!["value"] = null; break;
            case "numeric-enum": document["grants"]!["network"] = 0; break;
            case "unknown-enum": document["grants"]!["preopens"]![0]!["access"] = "execute"; break;
        }

        return document.ToJsonString();
    }
}
