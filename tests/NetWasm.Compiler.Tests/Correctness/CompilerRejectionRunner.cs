namespace NetWasm.Compiler.Tests.Correctness;

internal sealed record CompilerRejectionCase(
    MalformedInputMutation Input,
    string ReferencePath,
    string EntryType,
    string OutputDirectory,
    int Attempt,
    CompilerRejectionExpectation Expectation);

internal interface ICompilerRejectionRunner
{
    void Run(CompilerRejectionCase testCase);
}

internal sealed class CompilerRejectionRunner(
    IMalformedCompilationRunner compiler,
    ICompilerRejectionVerifier verifier,
    ICompilerRejectionFailureWriter failures) : ICompilerRejectionRunner
{
    public void Run(CompilerRejectionCase testCase)
    {
        ArgumentNullException.ThrowIfNull(testCase);
        ArgumentNullException.ThrowIfNull(testCase.Input);
        ArgumentNullException.ThrowIfNull(testCase.Expectation);
        ArgumentNullException.ThrowIfNull(testCase.Expectation.Diagnostic);
        ArgumentException.ThrowIfNullOrWhiteSpace(testCase.ReferencePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(testCase.EntryType);
        ArgumentException.ThrowIfNullOrWhiteSpace(testCase.OutputDirectory);
        ArgumentOutOfRangeException.ThrowIfNegative(testCase.Attempt);
        MalformedCompilationObservation? observation = null;
        try
        {
            observation = compiler.Compile(testCase.Input, testCase.ReferencePath,
                testCase.EntryType, testCase.OutputDirectory, testCase.Attempt);
            verifier.Verify(testCase.Expectation, observation);
        }
        catch (Exception cause)
        {
            string evidence;
            Exception? captureFailure = null;
            try
            {
                var directory = failures.Write(testCase, observation, cause);
                ArgumentException.ThrowIfNullOrWhiteSpace(directory);
                evidence = "; reproduction: " + directory;
            }
            catch (Exception exception)
            {
                captureFailure = exception;
                evidence = "; rejection evidence capture failed (original failure retained); incomplete archive (if allocated): " +
                    exception.Data["IncompleteEvidenceDirectory"];
            }
            var failure = new InvalidOperationException("Compiler rejection contract failed" + evidence, cause);
            if (captureFailure is not null) failure.Data["RejectionEvidenceCaptureFailure"] = captureFailure;
            throw failure;
        }
    }
}
