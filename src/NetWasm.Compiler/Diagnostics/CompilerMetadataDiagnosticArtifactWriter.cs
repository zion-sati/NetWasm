using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Diagnostics;

internal sealed class CompilerMetadataDiagnosticArtifactWriter(
    ICompilerDiagnosticArtifactPathResolver paths,
    ICompilerDiagnosticJsonArtifactWriter json,
    ICompilerMetadataSnapshotBuilder snapshot) : ICompilerMetadataDiagnosticArtifactWriter
{
    private readonly ICompilerDiagnosticArtifactPathResolver _paths =
        paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly ICompilerDiagnosticJsonArtifactWriter _json =
        json ?? throw new ArgumentNullException(nameof(json));
    private readonly ICompilerMetadataSnapshotBuilder _snapshot =
        snapshot ?? throw new ArgumentNullException(nameof(snapshot));

    public void WriteMetadata(
        CompilerOptions options,
        MetadataCompilationSnapshot metadata,
        IMethodBodyReader bodies,
        ISymbolFormatter symbols)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(bodies);
        ArgumentNullException.ThrowIfNull(symbols);
        var path = _paths.ResolvePath(options, "01-metadata.json");
        if (path is not null)
        {
            _json.WriteJson(path, _snapshot.BuildMetadata(metadata, bodies, symbols));
        }
    }
}
