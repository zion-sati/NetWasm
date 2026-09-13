using System;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.EntryPoints;

namespace NetWasm.Compiler.Diagnostics;

internal sealed class CompilerEntryDiagnosticArtifactWriter(
    ICompilerDiagnosticArtifactPathResolver paths,
    ICompilerDiagnosticJsonArtifactWriter json) : ICompilerEntryDiagnosticArtifactWriter
{
    private readonly ICompilerDiagnosticArtifactPathResolver _paths =
        paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly ICompilerDiagnosticJsonArtifactWriter _json =
        json ?? throw new ArgumentNullException(nameof(json));

    public void WriteEntry(CompilerOptions options, ISymbolFormatter symbols, CompilationEntry entry)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(symbols);
        ArgumentNullException.ThrowIfNull(entry);
        var path = _paths.ResolvePath(options, "02-entry.json");
        if (path is not null)
        {
            _json.WriteJson(path, new
            {
                EntryPoint = symbols.Format(entry.EntryPoint),
                Exports = entry.Exports.OrderBy(export => export.Name, StringComparer.Ordinal),
            });
        }
    }
}
