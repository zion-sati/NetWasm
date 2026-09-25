using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class CompilerRejectionRunnerTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void RoutesExactRequestOnceAndPreservesFirstFailure(int failureStage)
    {
        var cause = new InvalidOperationException("first failure");
        var compiler = new RecordingCompiler { Failure = failureStage == 1 ? cause : null };
        var verifier = new RecordingVerifier { Failure = failureStage == 2 ? cause : null };
        var testCase = Case();
        var writer = new RecordingFailures();
        var failure = Record.Exception(() => Runner(compiler, verifier, writer).Run(testCase));

        if (failureStage == 0)
        {
            Assert.Null(failure);
            Assert.Empty(writer.Calls);
        }
        else
        {
            Assert.IsType<InvalidOperationException>(failure);
            Assert.Same(cause, failure.InnerException);
            Assert.Contains("reproduction: retained-evidence", failure.Message, StringComparison.Ordinal);
            Assert.Equal((testCase, failureStage == 1 ? null : compiler.Observation, cause), Assert.Single(writer.Calls));
        }
        Assert.Equal((testCase.Input, testCase.ReferencePath, testCase.EntryType, testCase.OutputDirectory, testCase.Attempt), Assert.Single(compiler.Calls));
        if (failureStage == 1) Assert.Empty(verifier.Calls);
        else Assert.Equal((testCase.Expectation, compiler.Observation), Assert.Single(verifier.Calls));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void InvalidCaseDoesNotInvokeCompilerOrVerifier(int defect)
    {
        var compiler = new RecordingCompiler();
        var verifier = new RecordingVerifier();
        var writer = new RecordingFailures();
        var valid = Case();
        var testCase = defect switch
        {
            0 => null!,
            1 => valid with { Input = null! },
            2 => valid with { Expectation = null! },
            3 => valid with { Expectation = new(null!) },
            4 => valid with { ReferencePath = " " },
            5 => valid with { EntryType = "" },
            6 => valid with { OutputDirectory = null! },
            _ => valid with { Attempt = -1 },
        };
        Assert.ThrowsAny<ArgumentException>(() => Runner(compiler, verifier, writer).Run(testCase));
        Assert.Empty(compiler.Calls);
        Assert.Empty(verifier.Calls);
        Assert.Empty(writer.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CaptureErrorCannotReplaceOriginalRejectionFailure(bool emptyPath)
    {
        var cause = new IOException("compiler failure");
        var capture = new IOException("archive failure");
        var writer = new RecordingFailures { Failure = emptyPath ? null : capture, Path = emptyPath ? "" : "unused" };
        var compiler = new RecordingCompiler { Failure = cause };
        var error = Assert.Throws<InvalidOperationException>(() => Runner(compiler, new RecordingVerifier(), writer).Run(Case()));

        Assert.Same(cause, error.InnerException);
        Assert.Single(compiler.Calls);
        Assert.Single(writer.Calls);
        Assert.Contains("original failure retained", error.Message, StringComparison.Ordinal);
        if (emptyPath) Assert.IsType<ArgumentException>(error.Data["RejectionEvidenceCaptureFailure"]);
        else Assert.Same(capture, error.Data["RejectionEvidenceCaptureFailure"]);
    }

    private static CompilerRejectionCase Case() => new(new("case", "invariant", "input", "sha", 0, "00", "ff"),
        "reference", "Entry", "output", 3, new(new(DiagnosticCode.InvalidCil, "exact", "Entry::Run", 0)));
    private static ICompilerRejectionRunner Runner(RecordingCompiler compiler, RecordingVerifier verifier, RecordingFailures writer) =>
        Assert.IsAssignableFrom<ICompilerRejectionRunner>(new CompilerRejectionRunner(compiler, verifier, writer));

    private sealed class RecordingFailures : ICompilerRejectionFailureWriter
    {
        public List<(CompilerRejectionCase Case, MalformedCompilationObservation? Observation, Exception Cause)> Calls { get; } = [];
        public Exception? Failure { get; init; }
        public string Path { get; init; } = "retained-evidence";
        public string Write(CompilerRejectionCase testCase, MalformedCompilationObservation? observation, Exception failure)
        {
            Calls.Add((testCase, observation, failure));
            if (Failure is not null) throw Failure;
            return Path;
        }
    }

    private sealed class RecordingCompiler : IMalformedCompilationRunner
    {
        public Exception? Failure { get; init; }
        public List<(MalformedInputMutation Input, string Reference, string Entry, string Output, int Attempt)> Calls { get; } = [];
        public MalformedCompilationObservation Observation { get; } = new(QualifiedProcessCompletion.Exited, null, false, true, "") { ExitCode = 0 };
        public MalformedCompilationObservation Compile(MalformedInputMutation mutation, string referencePath, string entryType, string outputDirectory, int attempt)
        {
            Calls.Add((mutation, referencePath, entryType, outputDirectory, attempt));
            if (Failure is not null) throw Failure;
            return Observation;
        }
    }

    private sealed class RecordingVerifier : ICompilerRejectionVerifier
    {
        public Exception? Failure { get; init; }
        public List<(CompilerRejectionExpectation Expected, MalformedCompilationObservation Observed)> Calls { get; } = [];
        public void Verify(CompilerRejectionExpectation expected, MalformedCompilationObservation observed)
        {
            Calls.Add((expected, observed));
            if (Failure is not null) throw Failure;
        }
    }
}
