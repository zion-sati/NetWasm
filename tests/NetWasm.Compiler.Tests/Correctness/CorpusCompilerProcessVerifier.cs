using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed record CorpusCompilerInvocation(
    CorpusCompilation Compilation,
    WasmTarget Target,
    QualifiedProcessRequest Request,
    string RequestPath,
    string ResponsePath,
    string ModulePath,
    string? TracePath,
    ImmutableArray<string> ReferencePaths,
    ImmutableArray<string> SourcePaths);

internal interface ICorpusCompilerProcessVerifier
{
    void Verify(CorpusCompilerInvocation invocation, QualifiedProcessResult result);
}

internal sealed class CorpusCompilerProcessVerifier(ICorpusCompilerFailureWriter failures) : ICorpusCompilerProcessVerifier
{
    public void Verify(CorpusCompilerInvocation invocation, QualifiedProcessResult result)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(invocation.Request);
        ArgumentNullException.ThrowIfNull(result);
        if (result.Succeeded)
        {
            return;
        }
        string evidence;
        Exception? captureFailure = null;
        try
        {
            var directory = failures.Write(invocation, result);
            ArgumentException.ThrowIfNullOrWhiteSpace(directory);
            evidence = "; reproduction: " + directory;
        }
        catch (Exception exception)
        {
            captureFailure = exception;
            evidence = "; compiler evidence capture failed (original failure retained); incomplete archive (if allocated): " +
                exception.Data["IncompleteEvidenceDirectory"];
        }
        Exception failure = result.Completion == QualifiedProcessCompletion.TimedOut
            ? new TimeoutException($"NetWasm compiler exceeded {invocation.Request.Timeout}" + evidence, result.LaunchException)
            : new InvalidOperationException("NetWasm compiler process failed: " +
                $"completion={result.Completion}, exit={result.ExitCode}, " +
                result.StandardOutput + result.StandardError + evidence, result.LaunchException);
        if (captureFailure is not null)
        {
            failure.Data["CompilerEvidenceCaptureFailure"] = captureFailure;
        }
        throw failure;
    }
}
