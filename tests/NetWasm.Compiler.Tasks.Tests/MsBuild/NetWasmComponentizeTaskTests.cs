using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Node;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.ManagedExecutables;
using NetWasm.Compiler.Tasks.ComponentModel;
using NetWasm.Compiler.Tasks.Compilation;
using NetWasm.Compiler.Tasks.MsBuild;

namespace NetWasm.Compiler.Tasks.Tests.MsBuild;

public sealed class NetWasmComponentizeTaskTests
{
    [Theory]
    [InlineData("wasm32", "wasm32", "", "", "")]
    [InlineData("wasm64", "wasm64", "command", "1.28.1", "0.20.1")]
    public void ExecutesEveryTargetWidthThroughInjectedCapabilities(
        string target,
        string expectedWidth,
        string world,
        string jco,
        string shim)
    {
        var sessions = new RecordingSessionFactory();
        var inputs = new RecordingManifestInputsReader();
        var manifests = new RecordingManifestWriter();
        var entries = new RecordingEntryPointReader();
        var task = CreateTask(sessions, inputs, manifests, entries);
        task.Target = target;
        task.World = world;
        task.JcoVersion = jco;
        task.Preview2ShimVersion = shim;
        task.Optimization = "None";

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
                new BinaryenNativeTool(BinaryenToolIds.WasmMerge,
                    Path.GetFullPath("native-wasm-merge")),
            ],
            sessions.BinaryenConfiguration?.NativeTools);
        Assert.Equal(expectedWidth, sessions.Session.Request!.Target.Width);
        Assert.Equal(NullIfEmpty(world), sessions.Session.Request.World);
        Assert.Equal("app.wasm", sessions.Session.Request.CoreModulePath);
        Assert.Equal("runtime.wasm", sessions.Session.Request.RuntimeModulePath);
        Assert.Equal("interop.json", inputs.Request!.InteropManifestPath);
        Assert.Equal(NullIfEmpty(jco), inputs.Request.JcoVersion);
        Assert.Equal(NullIfEmpty(shim), inputs.Request.Preview2ShimVersion);
        Assert.Equal("app.dll", entries.Path);
        Assert.Equal(FinalWasmOptimization.None, sessions.Session.Request.Optimization);
        Assert.True(sessions.Session.IsDisposed);
        Assert.Same(sessions.Session.Manifest, manifests.Request!.Manifest);
        var component = Assert.Single(task.Components);
        Assert.Equal("Component", component.GetMetadata("Kind"));
        Assert.Equal(expectedWidth, component.GetMetadata("WasmTarget"));
        Assert.Equal("0.2", component.GetMetadata("WasiVersion"));
    }

    [Fact]
    public void ReportsComponentDiagnosticAndDisposesSession()
    {
        var sessions = new RecordingSessionFactory
        {
            Exception = new CompilerException(new(
                DiagnosticCode.ComponentToolchain,
                "component boundary")),
        };
        var build = new RecordingBuildEngine();
        var task = CreateTask(
            sessions,
            new RecordingManifestInputsReader(),
            new RecordingManifestWriter(),
            new RecordingEntryPointReader());
        task.BuildEngine = build;

        Assert.False(task.Execute());
        Assert.Empty(task.Components);
        Assert.True(sessions.Session.IsDisposed);
        Assert.Single(build.Errors);
        Assert.Contains("NW1010", build.Errors[0], StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsInvalidTargetWithoutCreatingSession()
    {
        var sessions = new RecordingSessionFactory();
        var build = new RecordingBuildEngine();
        var task = CreateTask(
            sessions,
            new RecordingManifestInputsReader(),
            new RecordingManifestWriter(),
            new RecordingEntryPointReader());
        task.Target = "unknown";
        task.BuildEngine = build;

        Assert.False(task.Execute());
        Assert.Null(sessions.WasmToolsCommand);
        Assert.Null(sessions.BinaryenConfiguration);
        Assert.Single(build.Errors);
        Assert.Contains("NWSDK021", build.Errors[0], StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsInvalidOptimizationWithoutCreatingSession()
    {
        var sessions = new RecordingSessionFactory();
        var build = new RecordingBuildEngine();
        var task = CreateTask(
            sessions,
            new RecordingManifestInputsReader(),
            new RecordingManifestWriter(),
            new RecordingEntryPointReader());
        task.Optimization = "Speed";
        task.BuildEngine = build;

        Assert.False(task.Execute());
        Assert.Null(sessions.WasmToolsCommand);
        Assert.Null(sessions.BinaryenConfiguration);
        Assert.Contains("NWSDK021", Assert.Single(build.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public void ConstructorsRejectMissingCapabilitiesAndComposeDefaults()
    {
        var sessions = new RecordingSessionFactory();
        var inputs = new RecordingManifestInputsReader();
        var manifests = new RecordingManifestWriter();
        var entries = new RecordingEntryPointReader();

        Assert.Throws<ArgumentNullException>(() =>
            new NetWasmComponentizeTask(
                null!, inputs, new ComponentWitWorldSelector(), manifests, entries));
        Assert.Throws<ArgumentNullException>(() =>
            new NetWasmComponentizeTask(
                sessions, null!, new ComponentWitWorldSelector(), manifests, entries));
        Assert.Throws<ArgumentNullException>(() =>
            new NetWasmComponentizeTask(sessions, inputs, null!, manifests, entries));
        Assert.Throws<ArgumentNullException>(() =>
            new NetWasmComponentizeTask(
                sessions, inputs, new ComponentWitWorldSelector(), null!, entries));
        Assert.Throws<ArgumentNullException>(() =>
            new NetWasmComponentizeTask(
                sessions, inputs, new ComponentWitWorldSelector(), manifests, null!));
        Assert.NotNull(new NetWasmComponentizeTask());
    }

    [Fact]
    public void SelectsReachableCapabilityWorldVariant()
    {
        var sessions = new RecordingSessionFactory();
        var inputs = new RecordingManifestInputsReader
        {
            Result = new ComponentManifestInputs(
                ComponentJavaScriptBoundary.Empty,
                ComponentAdapterVersions.None) with
            {
                WitImports =
                [
                    new HostInteropWitImport(
                        "wasi:http@0.2.11/outgoing-handler",
                        "handle"),
                ],
            },
        };
        var task = CreateTask(
            sessions,
            inputs,
            new RecordingManifestWriter(),
            new RecordingEntryPointReader());
        var variant = new TaskItem("http-command.wit");
        variant.SetMetadata("World", "http-command");
        variant.SetMetadata("ActivationInterfacePrefix", "wasi:http@0.2.11/");
        task.WitWorldVariants = [variant];

        Assert.True(task.Execute());

        Assert.Equal("http-command.wit", sessions.Session.Request!.WitPath);
        Assert.Equal("http-command", sessions.Session.Request.World);
        Assert.Equal("http-command.wit", task.SelectedWitPath);
        Assert.Equal("http-command", task.SelectedWorld);
    }

    private static NetWasmComponentizeTask CreateTask(
        IComponentBuildSessionFactory sessions,
        IComponentManifestInputsReader inputs,
        IComponentManifestWriter manifests,
        IManagedEntryPointReader entries) => new(
            sessions,
            inputs,
            new ComponentWitWorldSelector(),
            manifests,
            entries)
        {
            InputAssemblyPath = "app.dll",
            CoreModulePath = "app.wasm",
            RuntimeModulePath = "runtime.wasm",
            WitPath = "command.wit",
            World = "command",
            OutputPath = "component.wasm",
            ComponentManifestPath = "component.json",
            InteropManifestPath = "interop.json",
            WasmToolsNodePath = "node",
            WasmToolsCommandPath = "run-wasm-tools.mjs",
            WasmToolsModulePath = "wasm-tools.wasm",
            BinaryenWasmOptPath = "wasm-opt",
            BinaryenWasmMergePath = "wasm-merge",
            NativeBinaryenWasmOptPath = Path.GetFullPath("native-wasm-opt"),
            NativeBinaryenWasmMergePath = Path.GetFullPath("native-wasm-merge"),
            Target = "wasm32",
        };

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrEmpty(value) ? null : value;

    private sealed class RecordingSessionFactory : IComponentBuildSessionFactory
    {
        public Exception? Exception { get; init; }
        public ExternalToolCommand? WasmToolsCommand { get; private set; }
        public BinaryenToolRunnerConfiguration? BinaryenConfiguration { get; private set; }
        public RecordingSession Session { get; private set; } = new();

        public IComponentBuildSession Create(
            ExternalToolCommand wasmToolsCommand,
            BinaryenToolRunnerConfiguration binaryenConfiguration)
        {
            WasmToolsCommand = wasmToolsCommand;
            BinaryenConfiguration = binaryenConfiguration;
            Session = new() { Exception = Exception };
            return Session;
        }
    }

    private sealed class RecordingSession : IComponentBuildSession
    {
        public Exception? Exception { get; init; }
        public ComponentBuildRequest? Request { get; private set; }
        public bool IsDisposed { get; private set; }
        public ComponentManifest Manifest { get; private set; } = CreateManifest("wasm32");

        public ComponentManifest Build(ComponentBuildRequest request)
        {
            Request = request;
            if (Exception is not null)
            {
                throw Exception;
            }

            Manifest = CreateManifest(request.Target.Width);
            return Manifest;
        }

        public void Dispose() => IsDisposed = true;
    }

    private sealed class RecordingManifestInputsReader : IComponentManifestInputsReader
    {
        public ComponentManifestInputsRequest? Request { get; private set; }
        public ComponentManifestInputs Result { get; init; } =
            new(ComponentJavaScriptBoundary.Empty, ComponentAdapterVersions.None);

        public ComponentManifestInputs Read(ComponentManifestInputsRequest request)
        {
            Request = request;
            return Result;
        }
    }

    private sealed class RecordingManifestWriter : IComponentManifestWriter
    {
        public ComponentManifestWriteRequest? Request { get; private set; }

        public void Write(ComponentManifestWriteRequest request) => Request = request;
    }

    private sealed class RecordingEntryPointReader : IManagedEntryPointReader
    {
        public string? Path { get; private set; }

        public ManagedEntryPoint Read(string assemblyPath)
        {
            Path = assemblyPath;
            return new(
                "Program",
                "Main",
                0x06000001,
                new(
                    ManagedExecutableParameterShape.StringArray,
                    ManagedExecutableReturnShape.ExitCode));
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

    private static ComponentManifest CreateManifest(string target) => new(
        1,
        "wasi:cli@0.2.11",
        "command",
        target,
        "0.2",
        "utf8",
        "digest",
        "wasm-tools",
        [],
        [],
        [],
        []);
}
