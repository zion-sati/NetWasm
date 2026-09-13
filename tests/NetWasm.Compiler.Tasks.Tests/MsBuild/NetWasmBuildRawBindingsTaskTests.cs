using System.Collections.Immutable;
using Microsoft.Build.Framework;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Catalogs;
using NetWasm.Compiler.ComponentModel.Raw;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Tasks.Artifacts;
using NetWasm.Compiler.Tasks.ComponentModel;
using NetWasm.Compiler.Tasks.MsBuild;
using NetWasm.Compiler.Wasm;

namespace NetWasm.Compiler.Tasks.Tests.MsBuild;

public sealed class NetWasmBuildRawBindingsTaskTests
{
    [Theory]
    [InlineData("wasm32", WasmTarget.Wasm32)]
    [InlineData("wasm64", WasmTarget.Wasm64)]
    public void BuildsSelectedAdapterAndPublishesLogicalImports(
        string target,
        WasmTarget expectedTarget)
    {
        var metadata = new RecordingCompilerMetadataReader(target);
        var interop = new RecordingInteropReader();
        var sessions = new RecordingSessionFactory();
        var artifacts = new RecordingByteWriter();
        var task = CreateTask(metadata, interop, sessions, artifacts);
        task.Target = target;

        Assert.True(task.Execute());

        Assert.Equal("compiler.json", metadata.Path);
        Assert.Equal("interop.json", interop.Path);
        Assert.Equal(target, interop.Target);
        var session = Assert.IsType<RecordingSession>(sessions.Session);
        var request = Assert.IsType<RawBuildImportSourceValidationRequest>(session.Request);
        Assert.Equal(expectedTarget, request.Target);
        Assert.Equal("wit", request.WitPath);
        Assert.Equal("world", request.World);
        Assert.Equal("runtime-wit", request.RuntimeWitPath);
        Assert.Equal("runtime-world", request.RuntimeWorld);
        Assert.Equal("node", request.RuntimeInspection.NodePath);
        var wasmTools = Assert.IsType<ExternalToolCommand>(sessions.WasmToolsCommand);
        Assert.Equal("node", wasmTools.Executable);
        Assert.Collection(
            wasmTools.ArgumentPrefix,
            argument => Assert.Equal("--disable-warning=ExperimentalWarning", argument),
            argument => Assert.Equal("wasm-tools.mjs", argument),
            argument => Assert.Equal("wasm-tools.js", argument));
        Assert.Equal(Path.GetFullPath("runtime.wasm"), request.RuntimeInspection.ModulePath);
        Assert.Equal(Path.GetFullPath("final.wasm"), request.FinalInspection.ModulePath);
        Assert.True(session.IsDisposed);
        Assert.Equal("adapter.mjs", artifacts.Path);
        Assert.Equal([1, 2, 3], artifacts.Bytes);
        Assert.Equal("RawAdapter", Assert.Single(task.Adapters).GetMetadata("Kind"));
        Assert.Collection(
            task.RequiredImports,
            import =>
            {
                Assert.Equal("wasi:cli/environment@0.2.11", import.GetMetadata("Interface"));
                Assert.Equal("get-arguments", import.GetMetadata("Name"));
                Assert.Equal("[]", import.GetMetadata("Parameters"));
                Assert.Equal("[\"list<string>\"]", import.GetMetadata("Results"));
            },
            import =>
            {
                Assert.Equal("wasi:cli/environment@0.2.11", import.GetMetadata("Interface"));
                Assert.Equal("get-environment", import.GetMetadata("Name"));
            });
        Assert.Equal(
            "wasi:cli/environment@0.2.11",
            Assert.Single(task.RequiredImportModules).ItemSpec);
    }

    [Fact]
    public void RejectsMetadataTargetDriftBeforeCreatingSession()
    {
        var sessions = new RecordingSessionFactory();
        var build = new RecordingBuildEngine();
        var task = CreateTask(
            new RecordingCompilerMetadataReader("wasm64"),
            new RecordingInteropReader(),
            sessions,
            new RecordingByteWriter());
        task.BuildEngine = build;

        Assert.False(task.Execute());
        Assert.Empty(task.Adapters);
        Assert.Empty(task.RequiredImports);
        Assert.Empty(task.RequiredImportModules);
        Assert.Null(sessions.Session);
        Assert.Contains("NWSDK027", Assert.Single(build.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsUnknownTargetBeforeReadingMetadata()
    {
        var metadata = new RecordingCompilerMetadataReader("wasm32");
        var build = new RecordingBuildEngine();
        var task = CreateTask(
            metadata,
            new RecordingInteropReader(),
            new RecordingSessionFactory(),
            new RecordingByteWriter());
        task.Target = "unknown";
        task.BuildEngine = build;

        Assert.False(task.Execute());
        Assert.Null(metadata.Path);
        Assert.Contains("NWSDK027", Assert.Single(build.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsCompilerDiagnosticsAndClearsOutputs()
    {
        var sessions = new RecordingSessionFactory
        {
            Exception = new CompilerException(new(
                DiagnosticCode.ComponentToolchain,
                "synthetic raw binding failure")),
        };
        var build = new RecordingBuildEngine();
        var task = CreateTask(
            new RecordingCompilerMetadataReader("wasm32"),
            new RecordingInteropReader(),
            sessions,
            new RecordingByteWriter());
        task.BuildEngine = build;

        Assert.False(task.Execute());
        Assert.Empty(task.Adapters);
        Assert.Empty(task.RequiredImports);
        Assert.Empty(task.RequiredImportModules);
        Assert.Contains("NW1010", Assert.Single(build.Errors), StringComparison.Ordinal);
        Assert.True(Assert.IsType<RecordingSession>(sessions.Session).IsDisposed);
    }

    [Fact]
    public void NormalizesEmptyWorldSelections()
    {
        var sessions = new RecordingSessionFactory();
        var task = CreateTask(
            new RecordingCompilerMetadataReader("wasm32"),
            new RecordingInteropReader(),
            sessions,
            new RecordingByteWriter());
        task.World = " ";
        task.RuntimeWorld = string.Empty;

        Assert.True(task.Execute());

        var request = Assert.IsType<RawBuildImportSourceValidationRequest>(
            Assert.IsType<RecordingSession>(sessions.Session).Request);
        Assert.Null(request.World);
        Assert.Null(request.RuntimeWorld);
    }

    [Fact]
    public void ConstructorsRejectMissingCapabilitiesAndComposeDefaults()
    {
        var metadata = new RecordingCompilerMetadataReader("wasm32");
        var interop = new RecordingInteropReader();
        var sessions = new RecordingSessionFactory();
        var artifacts = new RecordingByteWriter();

        Assert.Throws<ArgumentNullException>(() =>
            new NetWasmBuildRawBindingsTask(null!, interop, sessions, artifacts));
        Assert.Throws<ArgumentNullException>(() =>
            new NetWasmBuildRawBindingsTask(metadata, null!, sessions, artifacts));
        Assert.Throws<ArgumentNullException>(() =>
            new NetWasmBuildRawBindingsTask(metadata, interop, null!, artifacts));
        Assert.Throws<ArgumentNullException>(() =>
            new NetWasmBuildRawBindingsTask(metadata, interop, sessions, null!));
        Assert.NotNull(new NetWasmBuildRawBindingsTask());
    }

    private static NetWasmBuildRawBindingsTask CreateTask(
        ICompilerBuildMetadataReader metadata,
        IHostInteropManifestReader interop,
        IRawBindingSessionFactory sessions,
        IByteArtifactWriter artifacts) => new(metadata, interop, sessions, artifacts)
        {
            CompilerMetadataPath = "compiler.json",
            InteropManifestPath = "interop.json",
            WitPath = "wit",
            World = "world",
            RuntimeWitPath = "runtime-wit",
            RuntimeWorld = "runtime-world",
            Target = "wasm32",
            RuntimeModulePath = "runtime.wasm",
            FinalModulePath = "final.wasm",
            NodePath = "node",
            WasmToolsCommandPath = "wasm-tools.mjs",
            WasmToolsModulePath = "wasm-tools.js",
            InspectionScriptPath = "inspect.mjs",
            BinaryenPath = "binaryen.js",
            AdapterPath = "adapter.mjs",
        };

    private sealed class RecordingCompilerMetadataReader(string target) :
        ICompilerBuildMetadataReader
    {
        public string? Path { get; private set; }

        public CompilerBuildMetadata Read(string path)
        {
            Path = path;
            return new(1, target, [],
                [new("host", "call", WasmFunctionType.Create(CliValueKind.Void))]);
        }
    }

    private sealed class RecordingInteropReader : IHostInteropManifestReader
    {
        public string? Path { get; private set; }
        public string? Target { get; private set; }

        public HostInteropManifest Read(string path, string target)
        {
            Path = path;
            Target = target;
            return new(1, target, new(0, 1, 0), new(4, 4, 8, 4, 8), [], []);
        }
    }

    private sealed class RecordingSessionFactory : IRawBindingSessionFactory
    {
        public RecordingSession? Session { get; private set; }
        public ExternalToolCommand? WasmToolsCommand { get; private set; }
        public CompilerException? Exception { get; init; }

        public IRawBindingSession Create(ExternalToolCommand wasmToolsCommand)
        {
            WasmToolsCommand = wasmToolsCommand;
            Session = new() { Exception = Exception };
            return Session;
        }
    }

    private sealed class RecordingSession : IRawBindingSession
    {
        public RawBuildImportSourceValidationRequest? Request { get; private set; }
        public bool IsDisposed { get; private set; }
        public CompilerException? Exception { get; init; }

        public RawBindingBuildResult Build(RawBuildImportSourceValidationRequest request)
        {
            Request = request;
            if (Exception is not null)
            {
                throw Exception;
            }
            return new(
                [1, 2, 3],
                [
                    new("wasi:cli/environment@0.2.11", "get-arguments", [], ["list<string>"]),
                    new("wasi:cli/environment@0.2.11", "get-environment", [], ["list<string>"]),
                ]);
        }

        public void Dispose() => IsDisposed = true;
    }

    private sealed class RecordingByteWriter : IByteArtifactWriter
    {
        public string? Path { get; private set; }
        public byte[]? Bytes { get; private set; }

        public void Write(string path, byte[] bytes)
        {
            Path = path;
            Bytes = bytes;
        }
    }

    private sealed class RecordingBuildEngine : IBuildEngine
    {
        public List<string> Errors { get; } = [];
        public bool ContinueOnError => false;
        public int LineNumberOfTaskNode => 0;
        public int ColumnNumberOfTaskNode => 0;
        public string ProjectFileOfTaskNode => string.Empty;
        public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e.Message ?? string.Empty);
        public void LogWarningEvent(BuildWarningEventArgs e) { }
        public void LogMessageEvent(BuildMessageEventArgs e) { }
        public void LogCustomEvent(CustomBuildEventArgs e) { }
        public bool BuildProjectFile(
            string projectFileName,
            string[] targetNames,
            System.Collections.IDictionary globalProperties,
            System.Collections.IDictionary targetOutputs) => true;
    }
}
