using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NetWasm.Hosting.Execution;

namespace NetWasm.Hosting.Tests.Execution;

public sealed class NetWasmExecutionResultReaderTests
{
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void ReadsEitherNewlineAndValidatesDecodedValues(string newline)
    {
        var cleanup = ExecutionResultFixture.Failure(NetWasmFailurePhase.Cleanup, "cleanup.failed", "Cleanup failed.");
        var original = ExecutionResultFixture.Failed(cleanupFailures: [cleanup]);
        var writer = new NetWasmExecutionResultWriter(new NetWasmExecutionResultValidator());
        var text = Encoding.UTF8.GetString(writer.Write(original)).Replace("\n", newline, StringComparison.Ordinal);
        var calls = 0;
        var subject = Assert.IsAssignableFrom<INetWasmExecutionResultReader>(new NetWasmExecutionResultReader(new ExecutionResultValidationStub(value =>
        {
            Assert.Equal(original.CompletionKind, value.CompletionKind);
            Assert.Equal(original.PrimaryFailure, value.PrimaryFailure);
            Assert.Equal<NetWasmExecutionFailure>(original.CleanupFailures, value.CleanupFailures);
            calls++;
        })));
        var result = subject.Read(Encoding.UTF8.GetBytes(text));
        Assert.Equal(original.PrimaryFailure, result.PrimaryFailure);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":1,\"schemaVersion\":2}")]
    [InlineData("{\"schemaVersion\":1,\"completionKind\":99}")]
    [InlineData("{\"schemaVersion\":1,\"completionKind\":\"future\"}")]
    public void RejectsInvalidJsonBeforeValidation(string json)
    {
        var subject = new NetWasmExecutionResultReader(new ExecutionResultValidationStub(_ => Assert.Fail("Must not validate malformed JSON.")));
        Assert.Throws<JsonException>(() => subject.Read(Encoding.UTF8.GetBytes(json)));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("missing-nullable")]
    [InlineData("missing-required")]
    [InlineData("null-required")]
    [InlineData("case")]
    [InlineData("duplicate")]
    [InlineData("nested-unknown")]
    [InlineData("nested-missing")]
    [InlineData("nested-null")]
    [InlineData("nested-duplicate")]
    [InlineData("nested-integer-enum")]
    public void RejectsMalformedMembersBeforeValidation(string mutation)
    {
        var document = JsonNode.Parse(new NetWasmExecutionResultWriter(new NetWasmExecutionResultValidator())
            .Write(ExecutionResultFixture.Failed()))!.AsObject();
        var json = mutation switch
        {
            "duplicate" => document.ToJsonString().Insert(1, "\"schemaVersion\":1,"),
            "nested-duplicate" => document.ToJsonString().Replace("\"code\":", "\"code\":\"duplicate\",\"code\":", StringComparison.Ordinal),
            _ => Mutate(document, mutation),
        };
        var subject = new NetWasmExecutionResultReader(new ExecutionResultValidationStub(_ => Assert.Fail("Must not validate malformed JSON.")));
        Assert.Throws<JsonException>(() => subject.Read(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void PropagatesSemanticValidationFailure()
    {
        var bytes = new NetWasmExecutionResultWriter(new NetWasmExecutionResultValidator()).Write(ExecutionResultFixture.Normal());
        var failure = new ArgumentException("Rejected result.");
        var subject = new NetWasmExecutionResultReader(new ExecutionResultValidationStub(_ => throw failure));
        Assert.Same(failure, Assert.Throws<ArgumentException>(() => subject.Read(bytes)));
    }

    [Fact]
    public void RejectsMissingValidator() => Assert.Throws<ArgumentNullException>(() => new NetWasmExecutionResultReader(null!));

    private static string Mutate(JsonObject document, string mutation)
    {
        switch (mutation)
        {
            case "unknown": document["testOutcome"] = "passed"; break;
            case "missing-nullable": document.Remove("exitCode"); break;
            case "missing-required": document.Remove("cleanupFailures"); break;
            case "null-required": document["cleanupFailures"] = null; break;
            case "case": document["CompletionKind"] = document["completionKind"]!.DeepClone(); document.Remove("completionKind"); break;
            case "nested-unknown": document["primaryFailure"]!["stack"] = "must not cross the transport"; break;
            case "nested-missing": document["primaryFailure"]!.AsObject().Remove("code"); break;
            case "nested-null": document["primaryFailure"]!["code"] = null; break;
            case "nested-integer-enum": document["primaryFailure"]!["phase"] = 1; break;
        }

        return document.ToJsonString();
    }
}
