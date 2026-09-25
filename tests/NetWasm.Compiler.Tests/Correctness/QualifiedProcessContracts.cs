using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

internal enum QualifiedProcessCompletion
{
    Exited,
    TimedOut,
    LaunchFailed,
}

internal enum QualifiedProcessTermination
{
    None,
    Graceful,
    Forced,
    Failed,
}

internal sealed record QualifiedProcessRequest(
    string FileName,
    ImmutableArray<string> Arguments,
    TimeSpan Timeout)
{
    public string? WorkingDirectory { get; init; }

    public ImmutableDictionary<string, string> EnvironmentVariables { get; init; } =
        ImmutableDictionary<string, string>.Empty;

    public Action<int, int>? Progress { get; init; }
}

internal sealed record QualifiedProcessResult(
    QualifiedProcessCompletion Completion,
    int? ExitCode,
    string StandardOutput,
    string StandardError,
    TimeSpan Elapsed)
{
    public Exception? LaunchException { get; init; }

    public QualifiedProcessTermination Termination { get; init; }

    public string? CleanupFailure { get; init; }

    public bool Succeeded =>
        Completion == QualifiedProcessCompletion.Exited && ExitCode == 0;
}

internal interface IQualifiedProcessRunner
{
    QualifiedProcessResult Run(
        QualifiedProcessRequest request,
        CancellationToken cancellationToken = default);
}
