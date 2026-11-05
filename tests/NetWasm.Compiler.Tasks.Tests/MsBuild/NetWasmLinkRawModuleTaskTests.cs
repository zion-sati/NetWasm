using NetWasm.Compiler.ComponentModel.Optimization;
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
    [InlineData("Auto", ManagedInliningHints.Auto)]
    [InlineData("IgnoreAggressive", ManagedInliningHints.IgnoreAggressive)]
    [InlineData("Preserve", ManagedInliningHints.Preserve)]
    public void PassesHintOverrideToNormalLinkSession(string value, ManagedInliningHints expected)
    {
        var sessions = new RecordingSessionFactory();
        var task = CreateTask(sessions);
        task.ManagedInliningHints = value;
        Assert.True(task.Execute());
        Assert.Equal(expected, sessions.Session.Request!.OptimizationOverrides?.ManagedInliningHints ?? ManagedInliningHints.Auto);
    }

    // The optimizer converges only where a project opts in, with or without a hint override.
    [Theory]
    [InlineData("Auto", false)]
    [InlineData("Auto", true)]
    [InlineData("Preserve", true)]
    public void PassesTheOptimizersConvergenceToTheLinkSession(string hints, bool converge)
    {
        var sessions = new RecordingSessionFactory();
        var task = CreateTask(sessions);
        task.ManagedInliningHints = hints;
        task.OptimizerConverge = converge;
        Assert.True(task.Execute());
        Assert.Equal(converge, sessions.Session.Request!.OptimizationOverrides?.Converge ?? false);
        Assert.Equal(hints == "Preserve" ? ManagedInliningHints.Preserve : ManagedInliningHints.Auto,
            sessions.Session.Request.OptimizationOverrides?.ManagedInliningHints ?? ManagedInliningHints.Auto);
    }

    [Fact]
    public void InvalidHintsFailBeforeCreatingSession()
    {
        var sessions = new RecordingSessionFactory();
        var build = new RecordingBuildEngine();
        var task = CreateTask(sessions);
        task.ManagedInliningHints = "invalid";
        task.BuildEngine = build;
        Assert.False(task.Execute());
        Assert.Null(sessions.WasmToolsCommand);
        Assert.Empty(task.Modules);
        Assert.Single(build.Errors);
    }

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
        task.Optimization = "None";
        task.InternalRuntimeExports = "wire metadata";
        task.InternalApplicationExports = "application metadata";

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
        Assert.Equal(
            [
                new BinaryenNativeTool(BinaryenToolIds.WasmOpt,
                    Path.GetFullPath("native-wasm-opt")),
            ],
            sessions.BinaryenConfiguration?.NativeTools);
        Assert.Equal("app.core.wasm", sessions.Session.Request!.ApplicationModulePath);
        Assert.Equal("runtime.wasm", sessions.Session.Request.RuntimeModulePath);
        Assert.Equal("linked.wasm", sessions.Session.Request.OutputPath);
        Assert.Equal(expectedWidth, sessions.Session.Request.Target.Width);
        Assert.Equal(FinalWasmOptimization.None, sessions.Session.Request.Optimization);
        Assert.Equal([new WasmInternalExport("native", 0)], sessions.Session.Request.InternalRuntimeExports.ToArray());
        Assert.Equal(
            [new WasmInternalExport("callback", 0)],
            sessions.Session.Request.InternalApplicationExports.ToArray());
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

    [Theory]
    [InlineData("Speed")]
    [InlineData("Size")]
    public void RejectsInvalidOptimizationBeforeCreatingSession(string optimization)
    {
        var sessions = new RecordingSessionFactory();
        var build = new RecordingBuildEngine();
        var task = CreateTask(sessions);
        task.Optimization = optimization;
        task.BuildEngine = build;

        Assert.False(task.Execute());
        Assert.Empty(task.Modules);
        Assert.Null(sessions.WasmToolsCommand);
        Assert.Null(sessions.BinaryenConfiguration);
        Assert.Contains("NWSDK026", Assert.Single(build.Errors), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("None", FinalWasmOptimization.None)]
    [InlineData("O0", FinalWasmOptimization.O0)]
    [InlineData("O1", FinalWasmOptimization.O1)]
    [InlineData("O2", FinalWasmOptimization.O2)]
    [InlineData("O3", FinalWasmOptimization.O3)]
    [InlineData("Os", FinalWasmOptimization.Os)]
    [InlineData("Oz", FinalWasmOptimization.Oz)]
    public void CanonicalizesOptimizationProperty(
        string value,
        FinalWasmOptimization expected)
    {
        var sessions = new RecordingSessionFactory();
        var task = CreateTask(sessions);
        task.Optimization = value;

        Assert.True(task.Execute());
        Assert.Equal(expected, sessions.Session.Request?.Optimization);
    }

    [Fact]
    public void ConstructorRejectsMissingCapabilityAndComposesDefaults()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new NetWasmLinkRawModuleTask(null!, new ExportReader(), new ManagedInliningHintsParser()));
        Assert.Throws<ArgumentNullException>(() => new NetWasmLinkRawModuleTask(new RecordingSessionFactory(), null!, new ManagedInliningHintsParser()));
        Assert.NotNull(new NetWasmLinkRawModuleTask());
        Assert.Throws<ArgumentNullException>(() => new NetWasmLinkRawModuleTask(new RecordingSessionFactory(), new ExportReader(), null!));
    }

    [Fact]
    public void UsesPortableBinaryenWhenNativeToolsAreUnavailable()
    {
        var sessions = new RecordingSessionFactory();
        var task = CreateTask(sessions);
        task.NativeBinaryenWasmOptPath = " ";

        Assert.True(task.Execute());
        Assert.Empty(sessions.BinaryenConfiguration?.NativeTools ?? []);
    }

    private static NetWasmLinkRawModuleTask CreateTask(
        IRawModuleLinkSessionFactory sessions) => new(sessions, new ExportReader(), new ManagedInliningHintsParser())
        {
            CoreModulePath = "app.core.wasm",
            RuntimeModulePath = "runtime.wasm",
            OutputPath = "linked.wasm",
            WasmToolsNodePath = "node",
            WasmToolsCommandPath = "run-wasm-tools.mjs",
            WasmToolsModulePath = "wasm-tools.wasm",
            BinaryenWasmOptPath = "wasm-opt",
            BinaryenWasmMergePath = "wasm-merge",
            NativeBinaryenWasmOptPath = Path.GetFullPath("native-wasm-opt"),
            Target = "wasm32",
        };

    private sealed class ExportReader : IInternalRuntimeExportReader
    {
        public System.Collections.Immutable.ImmutableArray<WasmInternalExport> Read(string metadata) =>
            string.IsNullOrEmpty(metadata)
                ? []
                : [new(metadata == "application metadata" ? "callback" : "native", 0)];
    }

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
