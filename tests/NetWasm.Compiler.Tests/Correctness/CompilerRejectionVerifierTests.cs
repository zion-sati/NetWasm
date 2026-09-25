using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class CompilerRejectionVerifierTests
{
    [Fact]
    public void MalformedResponseCauseRemainsVisible()
    {
        var cause = new System.Text.Json.JsonException("invalid response");
        var error = Assert.Throws<InvalidOperationException>(() => Verifier().Verify(new(Diagnostic()), Observation() with { ResponseFailure = cause }));
        Assert.Same(cause, error.InnerException);
        Assert.Equal("Compiler diagnostic response could not be read.", error.Message);
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void AcceptsOnlyExactRejectionWithRequiredArtifacts(bool requireSnapshot, bool hasSnapshot) =>
        Verifier().Verify(new(Diagnostic(), requireSnapshot), Observation() with { FailureSnapshotExists = hasSnapshot });

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    public void RejectsEveryWeakenedObservation(int defect)
    {
        var valid = Observation();
        var actual = defect switch
        {
            0 => valid with { Completion = QualifiedProcessCompletion.TimedOut },
            1 => valid with { Completion = QualifiedProcessCompletion.LaunchFailed },
            2 => valid with { ExitCode = 1 },
            3 => valid with { ExitCode = null },
            4 => valid with { Diagnostic = null },
            5 => valid with { Diagnostic = Diagnostic() with { Code = DiagnosticCode.InvalidCil } },
            6 => valid with { Diagnostic = Diagnostic() with { Message = "nearby but different" } },
            7 => valid with { Diagnostic = Diagnostic() with { Method = null } },
            8 => valid with { Diagnostic = Diagnostic() with { IlOffset = 1 } },
            9 => valid with { ModuleExists = true },
            _ => valid with { FailureSnapshotExists = false },
        };

        var failure = Assert.Throws<InvalidOperationException>(() => Verifier().Verify(new(Diagnostic()), actual));
        Assert.Equal(defect switch
        {
            <= 3 => "Expected diagnostic capture did not complete successfully.",
            <= 8 => "Compiler rejection did not match the exact expected diagnostic.",
            9 => "Compiler rejection left an output module.",
            _ => "Compiler rejection did not retain its required failure snapshot.",
        }, failure.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void MissingContractFailsImmediately(int missing) =>
        Assert.Throws<ArgumentNullException>(() => Verifier().Verify(
            missing == 0 ? null! : new(missing == 1 ? null! : Diagnostic()), missing == 2 ? null! : Observation()));

    private static CompilerDiagnostic Diagnostic() => new(DiagnosticCode.UnsupportedCil, "unsupported", "Entry::Run", 0);
    private static MalformedCompilationObservation Observation() =>
        new(QualifiedProcessCompletion.Exited, Diagnostic(), false, true, "") { ExitCode = 0 };
    private static ICompilerRejectionVerifier Verifier() => Assert.IsAssignableFrom<ICompilerRejectionVerifier>(new CompilerRejectionVerifier());
}
