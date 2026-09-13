using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NetWasm.Hosting.Execution;

namespace NetWasm.Hosting.Tests.Execution;

public sealed class ExecutionDescriptorReaderTests
{
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void ReadsEitherNewlineAndValidatesDecodedValues(string newline)
    {
        var original = DescriptorFixture.Create();
        var writer = Assert.IsAssignableFrom<IExecutionDescriptorWriter>(new ExecutionDescriptorWriter(new ExecutionDescriptorValidator()));
        var text = Encoding.UTF8.GetString(writer.Write(original)).Replace("\n", newline, StringComparison.Ordinal);
        var calls = 0;
        var subject = Assert.IsAssignableFrom<IExecutionDescriptorReader>(new ExecutionDescriptorReader(new DescriptorValidationStub(value =>
        {
            Assert.Equal(original with { ToolPackages = value.ToolPackages }, value);
            Assert.Equal<ExecutionToolPackage>(original.ToolPackages, value.ToolPackages);
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
    public void RejectsInvalidJsonBeforeValidation(string json)
    {
        var subject = Assert.IsAssignableFrom<IExecutionDescriptorReader>(new ExecutionDescriptorReader(new DescriptorValidationStub(_ => Assert.Fail("Must not validate malformed JSON."))));
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
    [InlineData("nested-duplicate")]
    public void RejectsMalformedMembersBeforeValidation(string mutation)
    {
        var writer = Assert.IsAssignableFrom<IExecutionDescriptorWriter>(new ExecutionDescriptorWriter(new ExecutionDescriptorValidator()));
        var document = JsonNode.Parse(writer.Write(DescriptorFixture.Create()))!.AsObject();
        var json = mutation switch
        {
            "duplicate" => document.ToJsonString().Insert(1, "\"schemaVersion\":1,"),
            "nested-duplicate" => document.ToJsonString().Replace("\"id\":", "\"id\":\"Duplicate\",\"id\":", StringComparison.Ordinal),
            _ => Mutate(document, mutation),
        };
        var subject = Assert.IsAssignableFrom<IExecutionDescriptorReader>(new ExecutionDescriptorReader(new DescriptorValidationStub(_ => Assert.Fail("Must not validate malformed JSON."))));
        Assert.Throws<JsonException>(() => subject.Read(Encoding.UTF8.GetBytes(json)));
    }

    private static string Mutate(JsonObject document, string mutation)
    {
        switch (mutation)
        {
            case "unknown": document["environment"] = "must not be here"; break;
            case "missing": document.Remove("launcherPath"); break;
            case "null": document["launcherPath"] = null; break;
            case "case": document["LauncherPath"] = document["launcherPath"]!.DeepClone(); document.Remove("launcherPath"); break;
            case "nested-unknown": document["toolPackages"]![0]!["extra"] = 1; break;
            case "nested-missing": document["toolPackages"]![0]!.AsObject().Remove("id"); break;
        }
        return document.ToJsonString();
    }

    [Fact]
    public void PropagatesSemanticValidationFailure()
    {
        var writer = Assert.IsAssignableFrom<IExecutionDescriptorWriter>(new ExecutionDescriptorWriter(new ExecutionDescriptorValidator()));
        var bytes = writer.Write(DescriptorFixture.Create());
        var failure = new ArgumentException("Rejected descriptor.");
        var subject = Assert.IsAssignableFrom<IExecutionDescriptorReader>(new ExecutionDescriptorReader(new DescriptorValidationStub(_ => throw failure)));
        Assert.Same(failure, Assert.Throws<ArgumentException>(() => subject.Read(bytes)));
    }

    [Fact]
    public void RejectsMissingValidator() => Assert.Throws<ArgumentNullException>(() => new ExecutionDescriptorReader(null!));
}
