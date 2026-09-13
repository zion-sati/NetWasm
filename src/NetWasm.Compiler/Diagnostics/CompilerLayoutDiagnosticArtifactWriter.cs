using System;
using NetWasm.Compiler.Layout;

namespace NetWasm.Compiler.Diagnostics;

internal sealed class CompilerLayoutDiagnosticArtifactWriter(
    ICompilerDiagnosticArtifactPathResolver paths,
    ICompilerDiagnosticJsonArtifactWriter json,
    ICompilerLayoutSnapshotBuilder snapshot) : ICompilerLayoutDiagnosticArtifactWriter
{
    private readonly ICompilerDiagnosticArtifactPathResolver _paths =
        paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly ICompilerDiagnosticJsonArtifactWriter _json =
        json ?? throw new ArgumentNullException(nameof(json));
    private readonly ICompilerLayoutSnapshotBuilder _snapshot =
        snapshot ?? throw new ArgumentNullException(nameof(snapshot));

    public void WriteLayouts(CompilerOptions options, ManagedLayoutSnapshot layouts)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(layouts);
        var path = _paths.ResolvePath(options, "04-layouts.json");
        if (path is not null)
        {
            _json.WriteJson(path, _snapshot.BuildLayouts(layouts));
        }
    }
}
