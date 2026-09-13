using System.Collections.Immutable;
using Microsoft.Build.Framework;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Tasks.Artifacts;
using NetWasm.Compiler.Tasks.Compilation;
using NetWasm.Compiler.Tasks.Composition;

namespace NetWasm.Compiler.Tasks.MsBuild;

public sealed class NetWasmCompileTask : CompilerArtifactManifestTaskBase
{
    private readonly IManagedModuleCompilationSessionFactory _compilers;
    private readonly ICompilerArtifactWriter _artifacts;
    private readonly ICompilerArtifactManifestTaskRequestBuilder _manifestRequestBuilder;
    private readonly ICompilerArtifactManifestBuilder _manifestBuilder;
    private readonly ICompilerArtifactManifestWriter _manifestWriter;

    public NetWasmCompileTask()
        : this(
            CompilerTaskComposition.CreateManagedModuleCompilationSessionFactory(),
            CompilerTaskComposition.CreateArtifactWriter(),
            CompilerTaskComposition.CreateArtifactManifestTaskRequestBuilder(),
            CompilerTaskComposition.CreateArtifactManifestBuilder(),
            CompilerTaskComposition.CreateArtifactManifestWriter())
    {
    }

    internal NetWasmCompileTask(
        IManagedModuleCompilationSessionFactory compilers,
        ICompilerArtifactWriter artifacts,
        ICompilerArtifactManifestTaskRequestBuilder manifestRequestBuilder,
        ICompilerArtifactManifestBuilder manifestBuilder,
        ICompilerArtifactManifestWriter manifestWriter)
    {
        _compilers = compilers ?? throw new ArgumentNullException(nameof(compilers));
        _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
        _manifestRequestBuilder = manifestRequestBuilder ?? throw new ArgumentNullException(nameof(manifestRequestBuilder));
        _manifestBuilder = manifestBuilder ?? throw new ArgumentNullException(nameof(manifestBuilder));
        _manifestWriter = manifestWriter ?? throw new ArgumentNullException(nameof(manifestWriter));
    }

    [Required]
    public string CoreModulePath { get; set; } = string.Empty;

    [Required]
    public string RuntimeLayoutPath { get; set; } = string.Empty;

    [Required]
    public string InteropManifestPath { get; set; } = string.Empty;

    [Required]
    public string CompilerMetadataPath { get; set; } = string.Empty;

    public string? DiagnosticTracePath { get; set; }
    public string? DiagnosticLogPath { get; set; }
    public string? WitPath { get; set; }
    public string? WitWorld { get; set; }
    public bool EmitStackTrace { get; set; }
    public string? StackTraceSymbolsPath { get; set; }

    [Required]
    public string WasmToolsNodePath { get; set; } = string.Empty;

    [Required]
    public string WasmToolsCommandPath { get; set; } = string.Empty;

    [Required]
    public string WasmToolsModulePath { get; set; } = string.Empty;

    public override bool Execute()
    {
        try
        {
            using var compiler = _compilers.Create(
                CompilerTaskComposition.CreateWasmToolsCommand(
                    WasmToolsNodePath,
                    WasmToolsCommandPath,
                    WasmToolsModulePath));
            var compilation = compiler.Compile(new(
                InputAssemblyPath,
                References.Select(static item => item.ItemSpec).ToImmutableArray(),
                Sources.Select(static item => item.ItemSpec).ToImmutableArray(),
                Target,
                DiagnosticTracePath,
                DiagnosticLogPath,
                EmitStackTrace,
                WitPath,
                WitWorld));
            _artifacts.Write(new(
                compilation,
                CoreModulePath,
                RuntimeLayoutPath,
                InteropManifestPath,
                CompilerMetadataPath,
                StackTraceSymbolsPath));
            if (!string.IsNullOrWhiteSpace(ArtifactManifestPath))
            {
                var buildResult = _manifestBuilder.Build(
                    _manifestRequestBuilder.Build(CreateManifestTaskInput()));
                _manifestWriter.Write(new(ArtifactManifestPath, buildResult.Manifest));
                SetResolvedArtifacts(buildResult);
            }
            return true;
        }
        catch (CompilerException exception)
        {
            Log.LogError(exception.Diagnostic.ToString());
            return false;
        }
        catch (Exception exception)
        {
            Log.LogErrorFromException(exception, showStackTrace: false);
            return false;
        }
    }
}
