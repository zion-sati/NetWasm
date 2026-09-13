using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Diagnostics;

internal sealed class CompilerProgramDiagnosticArtifactWriter(
    ICompilerDiagnosticArtifactPathResolver paths,
    ICompilerDiagnosticJsonArtifactWriter json,
    ICompilerProgramSnapshotBuilder snapshot) : ICompilerProgramDiagnosticArtifactWriter
{
    private readonly ICompilerDiagnosticArtifactPathResolver _paths =
        paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly ICompilerDiagnosticJsonArtifactWriter _json =
        json ?? throw new ArgumentNullException(nameof(json));
    private readonly ICompilerProgramSnapshotBuilder _snapshot =
        snapshot ?? throw new ArgumentNullException(nameof(snapshot));

    public void WriteProgram(CompilerOptions options, ISymbolFormatter symbols, ReachableProgram program)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(symbols);
        ArgumentNullException.ThrowIfNull(program);
        var path = _paths.ResolvePath(options, "03-program.json");
        if (path is not null)
        {
            _json.WriteJson(path, _snapshot.BuildProgram(symbols, program));
        }
    }
}
