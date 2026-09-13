using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NetWasm.Hosting.Deployment;

namespace NetWasm.Hosting.Tests.Deployment;

public sealed class DeploymentManifestReaderTests
{
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void ReadsEitherNewlineAndValidatesDecodedValues(string newline)
    {
        var original = ManifestFixture.Create();
        var writer = new DeploymentManifestWriter(new DeploymentManifestValidator());
        var text = Encoding.UTF8.GetString(writer.Write(original)).Replace("\n", newline, StringComparison.Ordinal);
        var calls = 0;
        var subject = Assert.IsAssignableFrom<IDeploymentManifestReader>(new DeploymentManifestReader(new ManifestValidationStub(value =>
        {
            Assert.Equal(original.SemanticBuildId, value.SemanticBuildId);
            Assert.Equal(original.DeploymentKind, value.DeploymentKind);
            Assert.Equal<DeploymentArtifact>(original.Artifacts, value.Artifacts);
            ManifestFixture.AssertFunctionsEqual(original.RequiredImports, value.RequiredImports);
            calls++;
        })));
        var result = subject.Read(Encoding.UTF8.GetBytes(text));
        Assert.Equal(original.BuildFingerprint, result.BuildFingerprint);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":1,\"schemaVersion\":2}")]
    [InlineData("{\"schemaVersion\":1,\"deploymentKind\":99}")]
    [InlineData("{\"schemaVersion\":1,\"deploymentKind\":\"future\"}")]
    public void RejectsInvalidJsonBeforeValidation(string json)
    {
        var subject = new DeploymentManifestReader(new ManifestValidationStub(_ => Assert.Fail("Must not validate malformed JSON.")));
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
    [InlineData("missing-runtime-features")]
    public void RejectsMalformedMembersBeforeValidation(string mutation)
    {
        var document = JsonNode.Parse(new DeploymentManifestWriter(new DeploymentManifestValidator()).Write(ManifestFixture.Create()))!.AsObject();
        var json = mutation switch
        {
            "duplicate" => document.ToJsonString().Insert(1, "\"schemaVersion\":1,"),
            "nested-duplicate" => document.ToJsonString().Replace("\"relativePath\":", "\"relativePath\":\"duplicate\",\"relativePath\":", StringComparison.Ordinal),
            _ => Mutate(document, mutation),
        };
        var subject = new DeploymentManifestReader(new ManifestValidationStub(_ => Assert.Fail("Must not validate malformed JSON.")));
        Assert.Throws<JsonException>(() => subject.Read(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void PropagatesSemanticValidationFailure()
    {
        var bytes = new DeploymentManifestWriter(new DeploymentManifestValidator()).Write(ManifestFixture.Create());
        var failure = new ArgumentException("Rejected manifest.");
        var subject = new DeploymentManifestReader(new ManifestValidationStub(_ => throw failure));
        Assert.Same(failure, Assert.Throws<ArgumentException>(() => subject.Read(bytes)));
    }

    [Fact]
    public void RejectsMissingValidator() => Assert.Throws<ArgumentNullException>(() => new DeploymentManifestReader(null!));

    private static string Mutate(JsonObject document, string mutation)
    {
        switch (mutation)
        {
            case "unknown": document["grants"] = "must not be here"; break;
            case "missing": document.Remove("executionContract"); break;
            case "null": document["executionContract"] = null; break;
            case "case": document["ExecutionContract"] = document["executionContract"]!.DeepClone(); document.Remove("executionContract"); break;
            case "nested-unknown": document["artifacts"]![0]!["absolutePath"] = "/private/tool"; break;
            case "nested-missing": document["versions"]!.AsObject().Remove("hosting"); break;
            case "nested-null": document["versions"]!["hosting"] = null; break;
            case "missing-runtime-features": document.Remove("runtimeFeatures"); break;
        }

        return document.ToJsonString();
    }
}
