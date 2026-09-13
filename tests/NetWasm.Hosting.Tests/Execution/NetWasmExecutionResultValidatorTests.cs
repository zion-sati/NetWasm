using NetWasm.Hosting.Execution;

namespace NetWasm.Hosting.Tests.Execution;

public sealed class NetWasmExecutionResultValidatorTests
{
    private readonly INetWasmExecutionResultValidator _subject =
        Assert.IsAssignableFrom<INetWasmExecutionResultValidator>(new NetWasmExecutionResultValidator());

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    public void AcceptsEverySignedNormalExitCode(int exitCode) => _subject.Validate(ExecutionResultFixture.Normal(exitCode));

    [Theory]
    [InlineData(NetWasmCompletionKind.ManagedFailure)]
    [InlineData(NetWasmCompletionKind.ManagedCancellation)]
    [InlineData(NetWasmCompletionKind.CallerCancellation)]
    [InlineData(NetWasmCompletionKind.ContractFailure)]
    [InlineData(NetWasmCompletionKind.HostFailure)]
    public void AcceptsEveryNonNormalCompletionWithPrimaryFailure(NetWasmCompletionKind kind) =>
        _subject.Validate(ExecutionResultFixture.Failed(kind));

    [Fact]
    public void AcceptsOrderedCleanupAndOutputFailures()
    {
        var first = ExecutionResultFixture.Failure(NetWasmFailurePhase.Cleanup, "scope.close", "Scope close failed.");
        var second = ExecutionResultFixture.Failure(NetWasmFailurePhase.Output, "output.flush", "Output flush failed.");
        _subject.Validate(ExecutionResultFixture.Failed(cleanupFailures: [first, second]));
    }

    [Fact]
    public void RejectsNullResult() => Assert.Throws<ArgumentNullException>(() => _subject.Validate(null!));

    [Theory]
    [MemberData(nameof(InvalidResults))]
    public void RejectsIncoherentOrMalformedResult(NetWasmExecutionResult result) =>
        Assert.ThrowsAny<ArgumentException>(() => _subject.Validate(result));

    public static TheoryData<NetWasmExecutionResult> InvalidResults()
    {
        var normal = ExecutionResultFixture.Normal();
        var failed = ExecutionResultFixture.Failed();
        var cleanup = ExecutionResultFixture.Failure(NetWasmFailurePhase.Cleanup, "cleanup.failed", "Cleanup failed.");
        var failure = failed.PrimaryFailure!;
        var data = new TheoryData<NetWasmExecutionResult>
        {
            normal with { SchemaVersion = 0 },
            normal with { SchemaVersion = 2 },
            normal with { CompletionKind = (NetWasmCompletionKind)99 },
            normal with { CleanupFailures = default },
            normal with { CleanupFailures = [null!] },
            normal with { ExitCode = null },
            normal with { PrimaryFailure = failure },
            normal with { CleanupFailures = [cleanup] },
            failed with { ExitCode = 0 },
            failed with { PrimaryFailure = null },
            failed with { PrimaryFailure = failure with { Phase = (NetWasmFailurePhase)99 } },
            failed with { CleanupFailures = [failure] },
            failed with { CleanupFailures = [cleanup with { Phase = (NetWasmFailurePhase)99 }] },
        };

        foreach (var code in new[] { null, "", " ", "1failure", "Failure", "bad_code", "bad\0code" })
        {
            data.Add(failed with { PrimaryFailure = failure with { Code = code! } });
            data.Add(failed with { CleanupFailures = [cleanup with { Code = code! }] });
        }

        foreach (var message in new[] { null, "", " ", " padded", "trailing ", "two\nlines", "bad\0message" })
        {
            data.Add(failed with { PrimaryFailure = failure with { Message = message! } });
            data.Add(failed with { CleanupFailures = [cleanup with { Message = message! }] });
        }

        return data;
    }
}
