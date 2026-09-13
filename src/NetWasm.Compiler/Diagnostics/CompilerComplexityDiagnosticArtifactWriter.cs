using System;

namespace NetWasm.Compiler.Diagnostics;

internal sealed class CompilerComplexityDiagnosticArtifactWriter(
    ICompilerDiagnosticArtifactPathResolver paths,
    ICompilerDiagnosticJsonArtifactWriter json) : ICompilerComplexityDiagnosticArtifactWriter
{
    private readonly ICompilerDiagnosticArtifactPathResolver _paths =
        paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly ICompilerDiagnosticJsonArtifactWriter _json =
        json ?? throw new ArgumentNullException(nameof(json));

    public void WriteComplexity(CompilerOptions options, CompilerComplexityReport report)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(report);
        var path = _paths.ResolvePath(options, "06-complexity.json");
        if (path is not null)
        {
            _json.WriteJson(path, report);
        }
    }
}
