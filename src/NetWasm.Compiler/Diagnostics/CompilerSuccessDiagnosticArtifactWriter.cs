using System;

namespace NetWasm.Compiler.Diagnostics;

internal sealed class CompilerSuccessDiagnosticArtifactWriter(
    ICompilerDiagnosticArtifactPathResolver paths,
    ICompilerDiagnosticJsonArtifactWriter json) : ICompilerSuccessDiagnosticArtifactWriter
{
    private readonly ICompilerDiagnosticArtifactPathResolver _paths =
        paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly ICompilerDiagnosticJsonArtifactWriter _json =
        json ?? throw new ArgumentNullException(nameof(json));

    public void WriteSuccess(CompilerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var success = _paths.ResolvePath(options, "07-success.json");
        if (success is null)
        {
            return;
        }
        _json.WriteJson(success, new { Status = "success", Stage = "complete" });
        _json.WriteJson(_paths.ResolvePath(options, "observations.json")!, new
        {
            Desktop = "recorded by the differential correctness harness",
            NetWasm = "compilation succeeded; execution is recorded by the caller",
        });
    }
}
