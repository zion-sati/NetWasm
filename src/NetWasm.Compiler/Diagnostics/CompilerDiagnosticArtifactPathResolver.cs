using System;
using System.IO;

namespace NetWasm.Compiler.Diagnostics;

internal sealed class CompilerDiagnosticArtifactPathResolver :
    ICompilerDiagnosticArtifactPathResolver
{
    public string? ResolvePath(CompilerOptions options, string artifactName)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactName);
        return options.DiagnosticTracePath is null
            ? null
            : Path.Combine(options.DiagnosticTracePath + ".passes", artifactName);
    }
}
