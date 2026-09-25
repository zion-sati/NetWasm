using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class CorpusCompilerProcessVerifierTests
{
    [Fact]
    public void SuccessfulProcessDoesNotCreateFailureEvidence()
    {
        var writer = new RecordingWriter();
        Verifier(writer).Verify(Invocation(), Result(0, 0));
        Assert.Empty(writer.Calls);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(0, 139)]
    [InlineData(0, null)]
    [InlineData(1, null)]
    [InlineData(2, null)]
    public void FailedProcessIsCapturedOnceAndNeverRelabelledSuccessful(int completion, int? exit)
    {
        var writer = new RecordingWriter();
        var invocation = Invocation();
        var cause = new InvalidOperationException("original launch cause");
        var result = Result(completion, exit) with { LaunchException = cause };

        var failure = Record.Exception(() => Verifier(writer).Verify(invocation, result));

        Assert.NotNull(failure);
        if (completion == 1)
        {
            Assert.IsType<TimeoutException>(failure);
            Assert.Contains(invocation.Request.Timeout.ToString(), failure.Message, StringComparison.Ordinal);
        }
        else
        {
            Assert.IsType<InvalidOperationException>(failure);
            Assert.Contains("completion=" + result.Completion, failure.Message, StringComparison.Ordinal);
            Assert.Contains("exit=" + exit, failure.Message, StringComparison.Ordinal);
            Assert.Contains("original outputoriginal error", failure.Message, StringComparison.Ordinal);
        }
        Assert.Same(cause, failure.InnerException);
        Assert.Contains("reproduction: /owned/failure", failure.Message, StringComparison.Ordinal);
        Assert.Equal((invocation, result), Assert.Single(writer.Calls));
        Assert.Empty(failure.Data);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void EvidenceCaptureFailureIsSecondaryToTheOriginalProcessFailure(bool timeout, bool emptyPath)
    {
        var cause = new IOException("capture failed");
        var writer = new RecordingWriter { Failure = emptyPath ? null : cause, Path = emptyPath ? "" : "/owned/failure" };
        var launch = new InvalidOperationException("original launch");
        var result = Result(timeout ? 1 : 2, null) with { LaunchException = launch };

        var failure = Record.Exception(() => Verifier(writer).Verify(Invocation(), result));

        Assert.NotNull(failure);
        Assert.Equal(timeout ? typeof(TimeoutException) : typeof(InvalidOperationException), failure.GetType());
        Assert.Same(launch, failure.InnerException);
        Assert.Contains("original failure retained", failure.Message, StringComparison.Ordinal);
        if (emptyPath)
        {
            Assert.IsType<ArgumentException>(failure.Data["CompilerEvidenceCaptureFailure"]);
        }
        else
        {
            Assert.Same(cause, failure.Data["CompilerEvidenceCaptureFailure"]);
        }
        Assert.Single(writer.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void MissingBoundaryDataFailsBeforeEvidenceCapture(int missing)
    {
        var writer = new RecordingWriter();
        var invocation = missing == 0 ? null : missing == 1 ? Invocation() with { Request = null! } : Invocation();

        Assert.Throws<ArgumentNullException>(() => Verifier(writer).Verify(invocation!, missing == 2 ? null! : Result(0, 1)));
        Assert.Empty(writer.Calls);
    }

    private static ICorpusCompilerProcessVerifier Verifier(RecordingWriter writer) =>
        Assert.IsAssignableFrom<ICorpusCompilerProcessVerifier>(new CorpusCompilerProcessVerifier(writer));

    private static QualifiedProcessResult Result(int completion, int? exit) => new(
        (QualifiedProcessCompletion)completion, exit, "original output", "original error", TimeSpan.FromSeconds(1));

    private static CorpusCompilerInvocation Invocation()
    {
        var artifact = new CorpusArtifact("input.dll", "", "sha", "", "producer", []);
        return new(new(new("Failure", "Failure", "", [0]), CilProfile.Emitted, artifact, artifact, "/run"),
            WasmTarget.Wasm64, new("dotnet", ["compiler", "request", "response"], TimeSpan.FromSeconds(3)),
            "request", "response", "module", null, [], []);
    }

    private sealed class RecordingWriter : ICorpusCompilerFailureWriter
    {
        public List<(CorpusCompilerInvocation Invocation, QualifiedProcessResult Result)> Calls { get; } = [];
        public Exception? Failure { get; init; }
        public string Path { get; init; } = "/owned/failure";

        public string Write(CorpusCompilerInvocation invocation, QualifiedProcessResult result, Exception? responseFailure = null)
        {
            Calls.Add((invocation, result));
            if (Failure is not null) throw Failure;
            return Path;
        }
    }
}
