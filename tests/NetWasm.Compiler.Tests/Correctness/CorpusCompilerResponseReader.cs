namespace NetWasm.Compiler.Tests.Correctness;

internal interface ICorpusCompilerResponseReader
{
    CorpusCompilerResponse Read(CorpusCompilerInvocation invocation, QualifiedProcessResult result);
}

internal sealed class CorpusCompilerResponseReader(
    ICorpusCompilerResponseParser parser,
    ICorpusCompilerFailureWriter failures) : ICorpusCompilerResponseReader
{
    public CorpusCompilerResponse Read(CorpusCompilerInvocation invocation, QualifiedProcessResult result)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(result);
        if (!result.Succeeded)
        {
            throw new ArgumentException("Compiler response reading requires a successful child process.", nameof(result));
        }
        try
        {
            return parser.Parse(File.ReadAllText(invocation.ResponsePath));
        }
        catch (Exception cause)
        {
            string evidence;
            Exception? captureFailure = null;
            try
            {
                var directory = failures.Write(invocation, result, cause);
                ArgumentException.ThrowIfNullOrWhiteSpace(directory);
                evidence = "; reproduction: " + directory;
            }
            catch (Exception exception)
            {
                captureFailure = exception;
                evidence = "; compiler evidence capture failed (original failure retained); incomplete archive (if allocated): " +
                    exception.Data["IncompleteEvidenceDirectory"];
            }
            var failure = new InvalidOperationException("Compiler response could not be read" + evidence, cause);
            if (captureFailure is not null)
            {
                failure.Data["CompilerEvidenceCaptureFailure"] = captureFailure;
            }
            throw failure;
        }
    }
}
