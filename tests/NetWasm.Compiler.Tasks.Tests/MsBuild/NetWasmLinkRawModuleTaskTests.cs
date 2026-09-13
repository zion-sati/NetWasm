using Microsoft.Build.Framework;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Node;
using NetWasm.Compiler.ComponentModel.Raw;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Tasks.ComponentModel;
using NetWasm.Compiler.Tasks.MsBuild;

namespace NetWasm.Compiler.Tasks.Tests.MsBuild;

public sealed class NetWasmLinkRawModuleTaskTests
{
    [Theory]
    [InlineData("wasm32", "wasm32")]
    [InlineData("wasm64", "wasm64")]
    public void LinksEveryTargetWidthThroughInjectedCapability(
        string target,
        string expectedWidth)
    {
        var sessions = new RecordingSessionFactory();
        var task = CreateTask(sessions);
        task.Target = target;

        Assert.True(task.Execute());

        Assert.Equal("node", sessions.WasmToolsCommand?.Executable);
        Assert.Equal(
            [
                "--disable-warning=ExperimentalWarning",
                "run-wasm-tools.mjs",
                "wasm-tools.wasm",
            ],
            sessions.WasmToolsCommand?.ArgumentPrefix);
        Assert.Equal("node", sessions.BinaryenConfiguration?.NodePath);
        Assert.Equal(
            [
                new BinaryenToolScript(BinaryenToolIds.WasmOpt, "wasm-opt"),
                new BinaryenToolScript(BinaryenToolIds.WasmMerge, "wasm-merge"),
            ],
            sessions.BinaryenConfiguration?.Scripts);
        Assert.Equal("app.core.wasm", sessions.Session.Request!.ApplicationModulePath);
        Assert.Equal("runtime.wasm", sessions.Session.Request.RuntimeModulePath);
        Assert.Equal("linked.wasm", sessions.Session.Request.OutputPath);
        Assert.Equal(expectedWidth, sessions.Session.Request.Target.Width);
        Assert.True(sessions.Session.IsDisposed);
        var module = Assert.Single(task.Modules);
        Assert.Equal("RawModule", module.GetMetadata("Kind"));
        Assert.Equal(expectedWidth, module.GetMetadata("WasmTarget"));
        Assert.Equal("0.2", module.GetMetadata("WasiVersion"));
    }

    [Fact]
    public void ReportsCompilerDiagnosticAndDisposesSession()
    {
        var sessions = new RecordingSessionFactory
        {
            Exception = new CompilerException(new(
                DiagnosticCode.ComponentToolchain,
                "link failed")),
        };
        var build = new RecordingBuildEngine();
        var task = CreateTask(sessions);
        task.BuildEngine = build;

        Assert.False(task.Execute());
        Assert.Empty(task.Modules);
        Assert.True(sessions.Session.IsDisposed);
        Assert.Contains("NW1010", Assert.Single(build.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsInvalidTargetBeforeCreatingSession()
    {
        var sessions = new RecordingSessionFactory();
        var build = new RecordingBuildEngine();
        var task = CreateTask(sessions);
        task.Target = "unknown";
        task.BuildEngine = build;

        Assert.False(task.Execute());
        Assert.Empty(task.Modules);
        Assert.Null(sessions.WasmToolsCommand);
        Assert.Null(sessions.BinaryenConfiguration);
        Assert.Contains("NWSDK026", Assert.Single(build.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public void ConstructorRejectsMissingCapabilityAndComposesDefaults()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new NetWasmLinkRawModuleTask(null!));
        Assert.NotNull(new NetWasmLinkRawModuleTask());
    }

    private static NetWasmLinkRawModuleTask CreateTask(
        IRawModuleLinkSessionFactory sessions) => new(sessions)
        {
            CoreModulePath = "app.core.wasm",
            RuntimeModulePath = "runtime.wasm",
            OutputPath = "linked.wasm",
            WasmToolsNodePath = "node",
            WasmToolsCommandPath = "run-wasm-tools.mjs",
            WasmToolsModulePath = "wasm-tools.wasm",
            BinaryenWasmOptPath = "wasm-opt",
            BinaryenWasmMergePath = "wasm-merge",
            Target = "wasm32",
        };

    private sealed class RecordingSessionFactory : IRawModuleLinkSessionFactory
    {
        public Exception? Exception { get; init; }
        public ExternalToolCommand? WasmToolsCommand { get; private set; }
        public BinaryenToolRunnerConfiguration? BinaryenConfiguration { get; private set; }
        public RecordingSession Session { get; private set; } = new();

        public IRawModuleLinkSession Create(
            ExternalToolCommand wasmToolsCommand,
            BinaryenToolRunnerConfiguration binaryenConfiguration)
        {
            WasmToolsCommand = wasmToolsCommand;
            BinaryenConfiguration = binaryenConfiguration;
            Session = new() { Exception = Exception };
            return Session;
        }
    }

    private sealed class RecordingSession : IRawModuleLinkSession
    {
        public Exception? Exception { get; init; }
        public RawModuleLinkRequest? Request { get; private set; }
        public bool IsDisposed { get; private set; }

        public void Link(RawModuleLinkRequest request)
        {
            Request = request;
            if (Exception is not null)
            {
                throw Exception;
            }
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
