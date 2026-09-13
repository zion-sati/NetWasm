using System.Text;
using System.Text.Json;
using NetWasm.Hosting.Execution;

namespace NetWasm.Hosting.Tests.Execution;

public sealed class NetWasmExecutionResultWriterTests
{
    [Fact]
    public void WritesDeterministicUtf8AndPreservesOrderedFailures()
    {
        var first = ExecutionResultFixture.Failure(NetWasmFailurePhase.Cleanup, "scope.close", "Scope close failed.");
        var second = ExecutionResultFixture.Failure(NetWasmFailurePhase.Output, "output.flush", "Output flush failed.");
        var result = ExecutionResultFixture.Failed(cleanupFailures: [first, second]);
        var subject = Assert.IsAssignableFrom<INetWasmExecutionResultWriter>(new NetWasmExecutionResultWriter(new NetWasmExecutionResultValidator()));
        var bytes = subject.Write(result);
        var text = Encoding.UTF8.GetString(bytes);
        Assert.Equal(bytes, subject.Write(result));
        Assert.StartsWith("{\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', text);
        Assert.Equal((byte)'{', bytes[0]);
        using var json = JsonDocument.Parse(bytes);
        Assert.Equal("hostFailure", json.RootElement.GetProperty("completionKind").GetString());
        Assert.Equal("scope.close", json.RootElement.GetProperty("cleanupFailures")[0].GetProperty("code").GetString());
        Assert.Equal("output.flush", json.RootElement.GetProperty("cleanupFailures")[1].GetProperty("code").GetString());
        var restored = new NetWasmExecutionResultReader(new NetWasmExecutionResultValidator()).Read(bytes);
        Assert.Equal(result.CompletionKind, restored.CompletionKind);
        Assert.Equal(result.PrimaryFailure, restored.PrimaryFailure);
        Assert.Equal<NetWasmExecutionFailure>(result.CleanupFailures, restored.CleanupFailures);
    }

    [Fact]
    public void PropagatesValidationFailureBeforeSerializing()
    {
        var result = ExecutionResultFixture.Normal();
        var failure = new ArgumentException("Rejected result.");
        var subject = new NetWasmExecutionResultWriter(new ExecutionResultValidationStub(value =>
        {
            Assert.Same(result, value);
            throw failure;
        }));
        Assert.Same(failure, Assert.Throws<ArgumentException>(() => subject.Write(result)));
    }

    [Fact]
    public void RejectsNullInputBeforeDelegation()
    {
        var subject = new NetWasmExecutionResultWriter(new ExecutionResultValidationStub(_ => Assert.Fail("Must not delegate.")));
        Assert.Throws<ArgumentNullException>(() => subject.Write(null!));
    }

    [Fact]
    public void RejectsMissingValidator() => Assert.Throws<ArgumentNullException>(() => new NetWasmExecutionResultWriter(null!));
}
