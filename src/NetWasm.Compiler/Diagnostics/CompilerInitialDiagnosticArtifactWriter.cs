using System;
using System.IO;
using System.Linq;

namespace NetWasm.Compiler.Diagnostics;

internal sealed class CompilerInitialDiagnosticArtifactWriter(
    ICompilerDiagnosticArtifactPathResolver paths,
    ICompilerDiagnosticJsonArtifactWriter json,
    ICompilerDiagnosticSourceArtifactCopier sources,
    ICompilerSourcePathProvider sourcePaths,
    ICompilerReproductionCommandBuilder reproductionCommands,
    ICompilerDiagnosticProvenanceProvider provenance,
    ICompilerDiagnosticFileExistenceReader files) : ICompilerInitialDiagnosticArtifactWriter
{
    private readonly ICompilerDiagnosticArtifactPathResolver _paths =
        paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly ICompilerDiagnosticJsonArtifactWriter _json =
        json ?? throw new ArgumentNullException(nameof(json));
    private readonly ICompilerDiagnosticSourceArtifactCopier _sources =
        sources ?? throw new ArgumentNullException(nameof(sources));
    private readonly ICompilerSourcePathProvider _sourcePaths =
        sourcePaths ?? throw new ArgumentNullException(nameof(sourcePaths));
    private readonly ICompilerReproductionCommandBuilder _reproductionCommands =
        reproductionCommands ?? throw new ArgumentNullException(nameof(reproductionCommands));
    private readonly ICompilerDiagnosticProvenanceProvider _provenance =
        provenance ?? throw new ArgumentNullException(nameof(provenance));
    private readonly ICompilerDiagnosticFileExistenceReader _files =
        files ?? throw new ArgumentNullException(nameof(files));

    public void WriteInitialArtifacts(CompilerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var optionsPath = _paths.ResolvePath(options, "00-options.json");
        if (optionsPath is null)
        {
            return;
        }
        var sourcePathsValue = _sourcePaths.Provide(options);
        _json.WriteJson(optionsPath, new
        {
            options.EntryAssemblyPath,
            ReferencePaths = options.ReferencePaths,
            options.EntryTypeName,
            options.EntryMethodName,
            options.Exports,
            options.Target,
            options.WitPath,
            options.WitWorld,
            SourcePaths = sourcePathsValue,
            Reproduce = _reproductionCommands.Build(options),
        });
        _json.WriteJson(_paths.ResolvePath(options, "00-toolchain.json")!,
            _provenance.ProvideProvenance(options));
        var sourceDescriptions = sourcePathsValue.Select((path, index) => new
        {
            Path = path,
            Exists = _files.Exists(path),
            Copy = _files.Exists(path)
                ? $"sources/{index:D4}-{System.IO.Path.GetFileName(path)}"
                : null,
        }).ToArray();
        foreach (var source in sourceDescriptions.Where(source => source.Exists))
        {
            _sources.CopySource(
                new FileInfo(source.Path),
                _paths.ResolvePath(options, source.Copy!)!);
        }
        _json.WriteJson(_paths.ResolvePath(options, "00-sources.json")!, sourceDescriptions);
    }
}
