using System;
using System.IO;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Diagnostics;

internal sealed class CompilerFailureDiagnosticArtifactWriter(
    ICompilerDiagnosticArtifactPathResolver paths,
    ICompilerDiagnosticJsonArtifactWriter json,
    ICompilerReproductionCommandBuilder reproductionCommands,
    ICompilerDiagnosticArtifactEnumerator artifacts) : ICompilerFailureDiagnosticArtifactWriter
{
    private readonly ICompilerDiagnosticArtifactPathResolver _paths =
        paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly ICompilerDiagnosticJsonArtifactWriter _json =
        json ?? throw new ArgumentNullException(nameof(json));
    private readonly ICompilerReproductionCommandBuilder _reproductionCommands =
        reproductionCommands ?? throw new ArgumentNullException(nameof(reproductionCommands));
    private readonly ICompilerDiagnosticArtifactEnumerator _artifacts =
        artifacts ?? throw new ArgumentNullException(nameof(artifacts));

    public void WriteFailure(CompilerOptions options, string stage, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);
        ArgumentNullException.ThrowIfNull(exception);
        var failure = _paths.ResolvePath(options, "failure.json");
        if (failure is null)
        {
            return;
        }
        var directory = Path.GetFullPath(options.DiagnosticTracePath + ".passes");
        _json.WriteJson(
            failure,
            new CompilerFailureArtifact(
                "failed",
                stage,
                exception.GetType().FullName,
                exception.Message,
                exception.StackTrace,
                exception is CompilerException compiler ? compiler.Diagnostic.ToString() : null,
                _reproductionCommands.Build(options),
                _artifacts.Enumerate(directory)),
            CompilerDiagnosticArtifactJsonContext.Default.CompilerFailureArtifact);
        _json.WriteJson(
            _paths.ResolvePath(options, "observations.json")!,
            new CompilerObservationsArtifact(
                "recorded by the differential correctness harness when available",
                $"compiler failed during {stage}"),
            CompilerDiagnosticArtifactJsonContext.Default.CompilerObservationsArtifact);
    }
}
