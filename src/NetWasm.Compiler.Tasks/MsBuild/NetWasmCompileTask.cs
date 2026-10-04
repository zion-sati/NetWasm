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
    private readonly ICompilerBuildMessageWriter _messages;

    public NetWasmCompileTask()
    {
        _messages = new CompilerBuildMessageWriter(Log);
        _compilers = CompilerTaskComposition.CreateManagedModuleCompilationSessionFactory(
            new CompilerBuildProgressReporter(_messages));
        _artifacts = CompilerTaskComposition.CreateArtifactWriter();
        _manifestRequestBuilder =
            CompilerTaskComposition.CreateArtifactManifestTaskRequestBuilder();
        _manifestBuilder = CompilerTaskComposition.CreateArtifactManifestBuilder();
        _manifestWriter = CompilerTaskComposition.CreateArtifactManifestWriter();
    }

    internal NetWasmCompileTask(
        IManagedModuleCompilationSessionFactory compilers,
        ICompilerArtifactWriter artifacts,
        ICompilerArtifactManifestTaskRequestBuilder manifestRequestBuilder,
        ICompilerArtifactManifestBuilder manifestBuilder,
        ICompilerArtifactManifestWriter manifestWriter,
        ICompilerBuildMessageWriter messages)
    {
        _compilers = compilers ?? throw new ArgumentNullException(nameof(compilers));
        _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
        _manifestRequestBuilder = manifestRequestBuilder ?? throw new ArgumentNullException(nameof(manifestRequestBuilder));
        _manifestBuilder = manifestBuilder ?? throw new ArgumentNullException(nameof(manifestBuilder));
        _manifestWriter = manifestWriter ?? throw new ArgumentNullException(nameof(manifestWriter));
        _messages = messages ?? throw new ArgumentNullException(nameof(messages));
    }

    [Required]
    public string CoreModulePath { get; set; } = string.Empty;

    [Required]
    public string RuntimeLayoutPath { get; set; } = string.Empty;

    [Required]
    public string InteropManifestPath { get; set; } = string.Empty;

    [Required]
    public string CompilerMetadataPath { get; set; } = string.Empty;

    [Required]
    public string NativeCallbackObjectPath { get; set; } = string.Empty;

    public string? DiagnosticTracePath { get; set; }
    public string? DiagnosticLogPath { get; set; }
    public string? WitPath { get; set; }
    public string? WitWorld { get; set; }
    public bool EmitStackTrace { get; set; }
    public bool StructuredDiagnostics { get; set; }
    public bool CompileAsLibrary { get; set; }
    public bool UseJavaScriptExportBoundary { get; set; }
    public string Optimization { get; set; } = "Oz";
    public string? StackTraceSymbolsPath { get; set; }
    public string? ExceptionTypeMapPath { get; set; }
    public bool NoLogo { get; set; }
    public string? PathMap { get; set; }

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
            if (!NoLogo)
            {
                _messages.Write($"NetWasm Compiler version {CompilerVersion}");
                _messages.Write("Copyright © 2026 Zion Sati");
            }
            _messages.Write(
                $"NetWasm: Compiling {Path.GetFileName(InputAssemblyPath)} for {Target}...");
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
                Optimization,
                Path.GetDirectoryName(CompilerMetadataPath),
                WitPath,
                WitWorld,
                CompileAsLibrary,
                UseJavaScriptExportBoundary,
                ProjectDirectory,
                PathMap,
                StructuredDiagnostics));
            _artifacts.Write(new(
                compilation,
                CoreModulePath,
                RuntimeLayoutPath,
                InteropManifestPath,
                CompilerMetadataPath,
                NativeCallbackObjectPath,
                StackTraceSymbolsPath)
            {
                ExceptionTypeMapPath = ExceptionTypeMapPath,
            });
            if (!string.IsNullOrWhiteSpace(ArtifactManifestPath))
            {
                var callbackArtifacts = compilation.NativeCallbackSupport is null
                    ? ImmutableArray<CompilerArtifactManifestTaskArtifact>.Empty
                    : [CreateNativeCallbackSupportArtifact(
                        NativeCallbackObjectPath)];
                var buildResult = _manifestBuilder.Build(
                    _manifestRequestBuilder.Build(CreateManifestTaskInput(callbackArtifacts)));
                _manifestWriter.Write(new(ArtifactManifestPath, buildResult.Manifest));
                SetResolvedArtifacts(buildResult);
            }
            _messages.Write($"NetWasm: Wrote {Path.GetFullPath(CoreModulePath)}.");
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
