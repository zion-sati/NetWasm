using Microsoft.Build.Framework;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Catalogs;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Tasks.ComponentModel;
using NetWasm.Compiler.Tasks.MsBuild;
using NetWasm.Compiler.Tasks.Artifacts;

namespace NetWasm.Compiler.Tasks.Tests.MsBuild;

public sealed class NetWasmProjectWitFunctionsTaskTests
{
    [Fact]
    public void ProjectsExactFunctionMetadataAndDisposesSession()
    {
        var sessions = new RecordingSessionFactory();
        var artifacts = new RecordingByteWriter();
        var task = new NetWasmProjectWitFunctionsTask(
            sessions,
            new WitWorkerContractWriter(artifacts))
        {
            WasmToolsNodePath = "node",
            WasmToolsCommandPath = "run-wasm-tools.mjs",
            WasmToolsModulePath = "wasm-tools.wasm",
            WitPath = "application.wasm",
            World = "example:app/command@1.0.0",
            ApplicationWitPath = "source-wit",
            ApplicationWorld = "example:app/application@1.0.0",
            SourceWorkerWorld = "example:app/worker@1.0.0",
            WorkerContractPath = Path.Combine(Path.GetTempPath(), "worker-contract.json"),
        };

        Assert.True(task.Execute());

        Assert.Equal("node", sessions.WasmToolsCommand?.Executable);
        Assert.Equal(
            [
                "--disable-warning=ExperimentalWarning",
                "run-wasm-tools.mjs",
                "wasm-tools.wasm",
            ],
            sessions.WasmToolsCommand?.ArgumentPrefix);
        Assert.Equal("application.wasm", sessions.Session.WitPath);
        Assert.Equal(task.World, sessions.Session.World);
        Assert.Equal(task.ApplicationWitPath, sessions.Session.ApplicationWitPath);
        Assert.Equal(task.ApplicationWorld, sessions.Session.ApplicationWorld);
        Assert.Equal(task.SourceWorkerWorld, sessions.Session.SourceWorkerWorld);
        Assert.True(sessions.Session.IsDisposed);
        var import = Assert.Single(task.RequiredImports);
        Assert.Equal("example:host/api@1.0.0/run", import.ItemSpec);
        Assert.Equal("example:host/api@1.0.0", import.GetMetadata("Interface"));
        Assert.Equal("run", import.GetMetadata("Name"));
        Assert.Equal("[\"string\"]", import.GetMetadata("Parameters"));
        Assert.Equal("[\"result<u32,string>\"]", import.GetMetadata("Results"));
        Assert.Equal(
            "example:host/api@1.0.0",
            Assert.Single(task.RequiredImportModules).ItemSpec);
        var export = Assert.Single(task.Exports);
        Assert.Equal("example:app/command@1.0.0", export.GetMetadata("Interface"));
        Assert.Equal("main", export.GetMetadata("Name"));
        Assert.Equal("[]", export.GetMetadata("Parameters"));
        Assert.Equal("[]", export.GetMetadata("Results"));
        var workerExport = Assert.Single(task.WorkerExports);
        Assert.Equal("add", workerExport.GetMetadata("Name"));
        Assert.Equal(Path.GetFullPath(task.WorkerContractPath), artifacts.Path);
        Assert.Contains("\"operation\":\"add\"", System.Text.Encoding.UTF8.GetString(artifacts.Bytes!),
            StringComparison.Ordinal);
        Assert.Contains("\"schemaVersion\":2", System.Text.Encoding.UTF8.GetString(artifacts.Bytes!),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsCompilerDiagnosticsAndClearsOutputs()
    {
        var sessions = new RecordingSessionFactory
        {
            Failure = new CompilerException(new(
                DiagnosticCode.ComponentContract,
                "projection failed")),
        };
        var build = new RecordingBuildEngine();
        var task = new NetWasmProjectWitFunctionsTask(
            sessions,
            new WitWorkerContractWriter(new RecordingByteWriter()))
        {
            BuildEngine = build,
            WasmToolsNodePath = "node",
            WasmToolsCommandPath = "run-wasm-tools.mjs",
            WasmToolsModulePath = "wasm-tools.wasm",
            WitPath = "application.wasm",
        };

        Assert.False(task.Execute());

        Assert.Empty(task.RequiredImports);
        Assert.Empty(task.RequiredImportModules);
        Assert.Empty(task.Exports);
        Assert.Empty(task.WorkerExports);
        Assert.True(sessions.Session.IsDisposed);
        Assert.Contains("NW1009", Assert.Single(build.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public void ConstructorRejectsMissingCapabilityAndComposesDefaults()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new NetWasmProjectWitFunctionsTask(
                null!,
                new WitWorkerContractWriter(new RecordingByteWriter())));
        Assert.Throws<ArgumentNullException>(() =>
            new NetWasmProjectWitFunctionsTask(
                new RecordingSessionFactory(),
                null!));
        Assert.NotNull(new NetWasmProjectWitFunctionsTask());
    }

    [Fact]
    public void DefaultCompilerCompositionSuppliesTheWorkerTypeProjection()
    {
        using var services = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
        var projector = services.GetRequiredService<IWitWorldFunctionProjector>();
        var function = new WitFunction("value", [], new WitTypeReference.Primitive("u32"), new("freestanding"));
        var world = new WitWorld(0, "application", "example:worker@1.0.0", [], [new("value", null, function)]);

        var contract = projector.Project(new([], [], [world], [], "{}"), world).WorkerContract;

        Assert.Equal(2, contract.SchemaVersion);
        Assert.Equal("value", Assert.Single(contract.Exports).Operation);
        Assert.Empty(contract.Types);
    }

    private sealed class RecordingSessionFactory : IWitFunctionProjectionSessionFactory
    {
        public Exception? Failure { get; init; }
        public ExternalToolCommand? WasmToolsCommand { get; private set; }
        public RecordingSession Session { get; private set; } = new();

        public IWitFunctionProjectionSession Create(ExternalToolCommand wasmToolsCommand)
        {
            WasmToolsCommand = wasmToolsCommand;
            Session = new() { Failure = Failure };
            return Session;
        }
    }

    private sealed class RecordingSession : IWitFunctionProjectionSession
    {
        public Exception? Failure { get; init; }
        public string? WitPath { get; private set; }
        public string? World { get; private set; }
        public string? ApplicationWitPath { get; private set; }
        public string? ApplicationWorld { get; private set; }
        public string? SourceWorkerWorld { get; private set; }
        public bool IsDisposed { get; private set; }

        public WitWorldFunctionProjection Project(
            string witPath,
            string? world,
            string? applicationWitPath,
            string? applicationWorld,
            string? sourceWorkerWorld)
        {
            WitPath = witPath;
            World = world;
            ApplicationWitPath = applicationWitPath;
            ApplicationWorld = applicationWorld;
            SourceWorkerWorld = sourceWorkerWorld;
            if (Failure is not null)
            {
                throw Failure;
            }
            return new(
                [new("example:host/api@1.0.0", "run", ["string"], ["result<u32,string>"])],
                [new("example:app/command@1.0.0", "main", [], [])],
                ["example:host/api@1.0.0"],
                new(
                    2,
                    "example:app/application@1.0.0",
                    [new(
                        "add",
                        "root",
                        "add",
                        null,
                        string.Empty,
                        "add",
                        "add",
                        [
                            new("left", new("primitive", "s32", null)),
                            new("right", new("primitive", "s32", null)),
                        ],
                        new("primitive", "s32", null),
                        new WitFunctionKind("freestanding"))],
                    [],
                    null));
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
        public void LogErrorEvent(BuildErrorEventArgs e) =>
            Errors.Add(e.Message ?? string.Empty);
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
