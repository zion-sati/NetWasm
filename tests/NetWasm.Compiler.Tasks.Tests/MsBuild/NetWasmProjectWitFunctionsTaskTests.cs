using Microsoft.Build.Framework;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Catalogs;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Tasks.ComponentModel;
using NetWasm.Compiler.Tasks.MsBuild;

namespace NetWasm.Compiler.Tasks.Tests.MsBuild;

public sealed class NetWasmProjectWitFunctionsTaskTests
{
    [Fact]
    public void ProjectsExactFunctionMetadataAndDisposesSession()
    {
        var sessions = new RecordingSessionFactory();
        var task = new NetWasmProjectWitFunctionsTask(sessions)
        {
            WasmToolsNodePath = "node",
            WasmToolsCommandPath = "run-wasm-tools.mjs",
            WasmToolsModulePath = "wasm-tools.wasm",
            WitPath = "application.wasm",
            World = "example:app/command@1.0.0",
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
        var task = new NetWasmProjectWitFunctionsTask(sessions)
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
        Assert.True(sessions.Session.IsDisposed);
        Assert.Contains("NW1009", Assert.Single(build.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public void ConstructorRejectsMissingCapabilityAndComposesDefaults()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new NetWasmProjectWitFunctionsTask(null!));
        Assert.NotNull(new NetWasmProjectWitFunctionsTask());
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
        public bool IsDisposed { get; private set; }

        public WitWorldFunctionProjection Project(string witPath, string? world)
        {
            WitPath = witPath;
            World = world;
            if (Failure is not null)
            {
                throw Failure;
            }
            return new(
                [new("example:host/api@1.0.0", "run", ["string"], ["result<u32,string>"])],
                [new("example:app/command@1.0.0", "main", [], [])],
                ["example:host/api@1.0.0"]);
        }

        public void Dispose() => IsDisposed = true;
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
