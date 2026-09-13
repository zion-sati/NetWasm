using System.Collections.Immutable;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Tasks.Artifacts;
using NetWasm.Compiler.Tasks.Compilation;
using NetWasm.Compiler.Tasks.Composition;
using NetWasm.Compiler.Tasks.MsBuild;
using NetWasm.Compiler.Tasks.Tests.TestSupport;

namespace NetWasm.Compiler.Tasks.Tests.MsBuild;

public sealed class NetWasmCompileTaskTests
{
    [Fact]
    public void DefaultConstructorCreatesTheProductionCapabilities()
    {
        var task = Assert.IsAssignableFrom<ITask>(new NetWasmCompileTask());

        Assert.Equal("wasm32", Assert.IsType<NetWasmCompileTask>(task).Target);
    }

    [Fact]
    public void ExecuteDelegatesCompilationAndArtifactWriting()
    {
        var compilation = CompilerTaskTestData.CreateCompilation("wasm64");
        var compiler = new RecordingCompiler(compilation);
        var compilers = new RecordingCompilationSessionFactory(compiler);
        var artifacts = new RecordingArtifactWriter();
        var build = new RecordingBuildEngine();
        var task = CreateTask(compilers, artifacts);
        task.BuildEngine = build;
        task.InputAssemblyPath = "application.dll";
        task.CoreModulePath = "application.core.wasm";
        task.RuntimeLayoutPath = "runtime-layout.json";
        task.InteropManifestPath = "interop.json";
        task.CompilerMetadataPath = "compiler-metadata.json";
        task.Target = "wasm64";
        task.References = [new TaskItem("library.dll"), new TaskItem("corelib.dll")];
        task.Sources = [new TaskItem("Program.cs")];
        task.DiagnosticTracePath = "trace.jsonl";
        task.DiagnosticLogPath = "compiler.log";
        task.WitPath = "compiler-wit";
        task.WitWorld = "netwasm:platform@1.0.0/platform";
        task.EmitStackTrace = true;
        task.StackTraceSymbolsPath = "application.stacktrace.json";

        Assert.True(task.Execute());
        Assert.Empty(build.Errors);
        Assert.Equal("node", compilers.Command?.Executable);
        Assert.Equal(
            [
                "--disable-warning=ExperimentalWarning",
                "run-wasm-tools.mjs",
                "wasm-tools.wasm",
            ],
            compilers.Command?.ArgumentPrefix);
        Assert.True(compilers.Session?.IsDisposed);
        var request = Assert.IsType<ManagedModuleCompileRequest>(compiler.Request);
        Assert.Equal(task.InputAssemblyPath, request.InputAssemblyPath);
        Assert.Equal(["library.dll", "corelib.dll"], request.ReferencePaths.ToArray());
        Assert.Equal(["Program.cs"], request.SourcePaths.ToArray());
        Assert.Equal(task.Target, request.Target);
        Assert.Equal(task.DiagnosticTracePath, request.DiagnosticTracePath);
        Assert.Equal(task.DiagnosticLogPath, request.DiagnosticLogPath);
        Assert.Equal(task.WitPath, request.WitPath);
        Assert.Equal(task.WitWorld, request.WitWorld);
        Assert.True(request.EmitStackTrace);
        var write = Assert.IsType<CompilerArtifactWriteRequest>(artifacts.Request);
        Assert.Same(compilation, write.Compilation);
        Assert.Equal(task.CoreModulePath, write.CoreModulePath);
        Assert.Equal(task.RuntimeLayoutPath, write.RuntimeLayoutPath);
        Assert.Equal(task.InteropManifestPath, write.InteropManifestPath);
        Assert.Equal(task.CompilerMetadataPath, write.CompilerMetadataPath);
        Assert.Equal(task.StackTraceSymbolsPath, write.StackTraceSymbolsPath);
    }

    [Fact]
    public void ExecuteWritesTheArtifactManifestAndPublishesResolvedMetadata()
    {
        var compilation = CompilerTaskTestData.CreateCompilation("wasm64");
        var manifest = new CompilerArtifactManifest(
            1,
            "build-id",
            "netwasm0.1",
            "wasm64",
            "none",
            "sdk",
            "compiler",
            "abi",
            "runtime",
            [],
            [new("CoreModule", "core.wasm", "application/wasm", "digest", 1, "wasm64", "netwasm0.1", "build-id")]);
        var result = new CompilerArtifactManifestBuildResult(
            manifest,
            [new(
                "application.core.wasm",
                manifest.Artifacts[0])]);
        var manifestBuilder = new RecordingManifestBuilder(result);
        var manifestWriter = new RecordingManifestWriter();
        var task = CreateTask(
            new RecordingCompilationSessionFactory(new RecordingCompiler(compilation)),
            new RecordingArtifactWriter(),
            new RecordingManifestRequestBuilder(),
            manifestBuilder,
            manifestWriter);
        task.InputAssemblyPath = "application.dll";
        task.CoreModulePath = "application.core.wasm";
        task.RuntimeLayoutPath = "runtime-layout.json";
        task.InteropManifestPath = "interop.json";
        task.CompilerMetadataPath = "compiler-metadata.json";
        task.ArtifactManifestPath = "manifest.json";
        task.ProjectDirectory = ".";

        Assert.True(task.Execute());
        Assert.Same(result.Manifest, manifestWriter.Request!.Manifest);
        Assert.Equal(task.ArtifactManifestPath, manifestWriter.Request.Path);
        var resolved = Assert.Single(task.ResolvedArtifacts);
        Assert.Equal("CoreModule", resolved.GetMetadata("Kind"));
        Assert.Equal("application.core.wasm", resolved.GetMetadata("RelativePath"));
        Assert.Equal("core.wasm", resolved.GetMetadata("ManifestPath"));
        Assert.Equal("digest", resolved.GetMetadata("Digest"));
        Assert.Equal("build-id", resolved.GetMetadata("SemanticBuildId"));
        Assert.Equal("PreserveNewest", resolved.GetMetadata("CopyToPublishDirectory"));
    }

    [Fact]
    public void ExecuteReportsACompilerDiagnostic()
    {
        var diagnostic = new CompilerDiagnostic(
            DiagnosticCode.InvalidEntryPoint,
            "managed entry point is invalid");
        var build = new RecordingBuildEngine();
        var artifacts = new RecordingArtifactWriter();
        var task = CreateTask(
            new RecordingCompilationSessionFactory(
                new ThrowingCompiler(new CompilerException(diagnostic))),
            artifacts);
        task.BuildEngine = build;

        Assert.False(task.Execute());
        Assert.Equal(diagnostic.ToString(), Assert.Single(build.Errors).Message);
        Assert.Null(artifacts.Request);
    }

    [Fact]
    public void ExecuteReportsAnUnexpectedFailureWithoutAStackTrace()
    {
        var build = new RecordingBuildEngine();
        var artifacts = new RecordingArtifactWriter();
        var task = CreateTask(
            new RecordingCompilationSessionFactory(
                new ThrowingCompiler(new InvalidOperationException("unexpected failure"))),
            artifacts);
        task.BuildEngine = build;

        Assert.False(task.Execute());
        var error = Assert.Single(build.Errors);
        Assert.Contains("unexpected failure", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Environment.NewLine, error.Message, StringComparison.Ordinal);
        Assert.Null(artifacts.Request);
    }

    [Fact]
    public void ConstructorRejectsMissingCapabilities()
    {
        var compilers = new RecordingCompilationSessionFactory(
            new RecordingCompiler(CompilerTaskTestData.CreateCompilation()));
        var artifacts = new RecordingArtifactWriter();
        var requests = new RecordingManifestRequestBuilder();
        var builders = new RecordingManifestBuilder(new(
            new(1, "build", "profile", "wasm32", "none", "sdk", "compiler", "abi", "runtime", [], []),
            []));
        var writers = new RecordingManifestWriter();

        Assert.Throws<ArgumentNullException>(() =>
            new NetWasmCompileTask(null!, artifacts, requests, builders, writers));
        Assert.Throws<ArgumentNullException>(() =>
            new NetWasmCompileTask(compilers, null!, requests, builders, writers));
        Assert.Throws<ArgumentNullException>(() =>
            new NetWasmCompileTask(compilers, artifacts, null!, builders, writers));
        Assert.Throws<ArgumentNullException>(() =>
            new NetWasmCompileTask(compilers, artifacts, requests, null!, writers));
        Assert.Throws<ArgumentNullException>(() =>
            new NetWasmCompileTask(compilers, artifacts, requests, builders, null!));
    }

    private static NetWasmCompileTask CreateTask(
        IManagedModuleCompilationSessionFactory compilers,
        ICompilerArtifactWriter artifacts,
        ICompilerArtifactManifestTaskRequestBuilder? manifestRequests = null,
        ICompilerArtifactManifestBuilder? manifestBuilder = null,
        ICompilerArtifactManifestWriter? manifestWriter = null) => new(
            compilers,
            artifacts,
            manifestRequests ?? new RecordingManifestRequestBuilder(),
            manifestBuilder ?? CompilerTaskComposition.CreateArtifactManifestBuilder(),
            manifestWriter ?? CompilerTaskComposition.CreateArtifactManifestWriter())
        {
            WasmToolsNodePath = "node",
            WasmToolsCommandPath = "run-wasm-tools.mjs",
            WasmToolsModulePath = "wasm-tools.wasm",
        };

    private sealed class RecordingCompiler(ManagedModuleCompilation result) : IManagedModuleCompiler
    {
        public ManagedModuleCompileRequest? Request { get; private set; }

        public ManagedModuleCompilation Compile(ManagedModuleCompileRequest request)
        {
            Request = request;
            return result;
        }
    }

    private sealed class ThrowingCompiler(Exception failure) : IManagedModuleCompiler
    {
        public ManagedModuleCompilation Compile(ManagedModuleCompileRequest request) =>
            throw failure;
    }

    private sealed class RecordingCompilationSessionFactory(
        IManagedModuleCompiler compiler) : IManagedModuleCompilationSessionFactory
    {
        public ExternalToolCommand? Command { get; private set; }
        public RecordingCompilationSession? Session { get; private set; }

        public IManagedModuleCompilationSession Create(ExternalToolCommand wasmToolsCommand)
        {
            Command = wasmToolsCommand;
            Session = new RecordingCompilationSession(compiler);
            return Session;
        }
    }

    private sealed class RecordingCompilationSession(
        IManagedModuleCompiler compiler) : IManagedModuleCompilationSession
    {
        public bool IsDisposed { get; private set; }

        public ManagedModuleCompilation Compile(ManagedModuleCompileRequest request) =>
            compiler.Compile(request);

        public void Dispose() => IsDisposed = true;
    }

    private sealed class RecordingArtifactWriter : ICompilerArtifactWriter
    {
        public CompilerArtifactWriteRequest? Request { get; private set; }

        public void Write(CompilerArtifactWriteRequest request) => Request = request;
    }

    private sealed class RecordingManifestRequestBuilder : ICompilerArtifactManifestTaskRequestBuilder
    {
        public CompilerArtifactManifestBuildRequest Build(CompilerArtifactManifestTaskInput input) => new(
            input.ManifestPath,
            input.ProjectDirectory,
            input.Profile,
            input.Target,
            input.FeatureSet,
            input.SdkVersion,
            input.CompilerVersion,
            input.RuntimeAbiVersion,
            input.RuntimeVersion,
            [],
            []);
    }

    private sealed class RecordingManifestBuilder(CompilerArtifactManifestBuildResult result) : ICompilerArtifactManifestBuilder
    {
        public CompilerArtifactManifestBuildResult Build(CompilerArtifactManifestBuildRequest request) => result;
    }

    private sealed class RecordingManifestWriter : ICompilerArtifactManifestWriter
    {
        public CompilerArtifactManifestWriteRequest? Request { get; private set; }

        public void Write(CompilerArtifactManifestWriteRequest request) => Request = request;
    }

    private sealed class RecordingBuildEngine : IBuildEngine
    {
        public List<BuildErrorEventArgs> Errors { get; } = [];
        public int ColumnNumberOfTaskNode => 0;
        public bool ContinueOnError => false;
        public int LineNumberOfTaskNode => 0;
        public string ProjectFileOfTaskNode => string.Empty;

        public bool BuildProjectFile(
            string projectFileName,
            string[] targetNames,
            System.Collections.IDictionary globalProperties,
            System.Collections.IDictionary targetOutputs) => false;

        public void LogCustomEvent(CustomBuildEventArgs e)
        {
        }

        public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e);

        public void LogMessageEvent(BuildMessageEventArgs e)
        {
        }

        public void LogWarningEvent(BuildWarningEventArgs e)
        {
        }
    }
}
