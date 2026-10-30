using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using NetWasm.Compiler.Tasks.MsBuild;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tasks.Tests.MsBuild;

public sealed class NetWasmCompileTaskTests
{
    // nwc.dll, which this test project references, beside the test assembly.
    private static readonly string Nwc = Path.Combine(AppContext.BaseDirectory, "nwc.dll");

    [Fact]
    public void WritesEveryOptionToTheResponseFile()
    {
        var task = CreateTask();
        task.NoLogo = true;
        task.CompileAsLibrary = true;
        task.Target = "wasm64";
        task.Optimization = "O3";
        task.References =
        [
            new TaskItem("CoreLib.dll"), new TaskItem("lib dir/Other.dll"), new TaskItem("lib/NetWasm,Version=v0.1/Lib.dll"),
        ];
        task.Sources = [new TaskItem("Program.cs")];
        task.ExceptionTypeMapPath = "app.exceptions.json";
        task.EmitStackTrace = true;
        task.StackTraceSymbolsPath = "app.stacktrace.json";
        task.WitPath = "app.wit";
        task.WitWorld = "example:app/app";
        task.ImportModules = [new TaskItem("host_ui")];
        task.DiagnosticTracePath = "trace.json";
        task.DiagnosticLogPath = "compiler.jsonl";
        task.UseJavaScriptExportBoundary = true;
        task.StructuredDiagnostics = true;
        task.ProjectDirectory = "/project";
        task.PathMap = "/project=/_/";
        task.ManagedMethodInlining = "Disabled";
        task.ManagedLoopRotation = "Enabled";
        task.ManagedAllocationElimination = string.Empty;
        task.ManagedLoopVectorization = "Disabled";

        Assert.Equal(
            [
                "-nologo", "App.dll", "-target:library", "-platform:wasm64", "-optimize:O3", "-out:app.wasm",
                "-r:CoreLib.dll", "-r:\"lib dir/Other.dll\"", "-r:\"lib/NetWasm,Version=v0.1/Lib.dll\"",
                "-source:Program.cs", "-runtimelayout:layout.json",
                "-interopmanifest:interop.json", "-compilermetadata:metadata.json", "-callbackobject:app.callbacks.o",
                "-exceptiontypemap:app.exceptions.json", "-stacktracesymbols:app.stacktrace.json", "-wit:app.wit",
                "-world:example:app/app", "-importmodule:host_ui", "-diagnostictrace:trace.json",
                "-diagnosticlog:compiler.jsonl", "-jsexportboundary+", "-structureddiagnostics+",
                "-projectdirectory:/project", "-pathmap:/project=/_/", "-intermediatedirectory:obj/netwasm/",
                "-wasmtoolsnode:node", "-wasmtoolscommand:run.mjs", "-wasmtoolsmodule:wasm-tools.wasm",
                "-managedmethodinlining:Disabled", "-managedlooprotation:Enabled", "-managedloopvectorization:Disabled",
            ],
            Lines(task.ResponseFileCommands));
    }

    [Fact]
    public void LeavesOptionalOptionsToNwcDefaults()
    {
        var task = CreateTask();
        task.StackTraceSymbolsPath = "app.stacktrace.json";

        Assert.Equal(
            [
                "App.dll", "-target:exe", "-platform:wasm32", "-optimize:Oz", "-out:app.wasm",
                "-runtimelayout:layout.json", "-interopmanifest:interop.json", "-compilermetadata:metadata.json",
                "-callbackobject:app.callbacks.o", "-intermediatedirectory:obj/netwasm/", "-wasmtoolsnode:node",
                "-wasmtoolscommand:run.mjs", "-wasmtoolsmodule:wasm-tools.wasm",
            ],
            Lines(task.ResponseFileCommands));
    }

    [Fact]
    public void RunsNwcDirectlyOrItsAssemblyOnTheDotNetHost()
    {
        var native = CreateTask();
        native.CompilerPath = "/tools/bin/nwc";
        var managed = CreateTask();
        managed.CompilerPath = "/build/nwc.dll";

        Assert.Equal("/tools/bin/nwc", native.FullPathToTool);
        Assert.Empty(native.CommandLineCommands);
        Assert.Equal("dotnet", managed.FullPathToTool);
        Assert.Equal("/build/nwc.dll", managed.CommandLineCommands);
    }

    [Fact]
    public void CompilesThroughNwcAndLogsItsOutput()
    {
        using var assets = TestAssets.Create();
        var library = assets.CompileSource("Worker", """
            using System.Runtime.InteropServices.JavaScript;
            public static class Worker
            {
                [JSExport("add")]
                public static int Add(int left, int right) => left + right;
            }
            """);
        // A package's TFM folder, such as lib/NetWasm,Version=v0.1, holds a comma
        // that a reference list would otherwise split at.
        var coreLib = Path.Combine(assets.Directory, "packages dir", "NetWasm,Version=v0.1", "NetWasm.CoreLib.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(coreLib)!);
        File.Copy(assets.CoreLib, coreLib);
        var build = new RecordingBuildEngine();
        var task = CreateTask(assets.Directory);
        task.BuildEngine = build;
        task.CompilerPath = Nwc;
        task.InputAssemblyPath = library;
        task.References = [new TaskItem(coreLib)];
        task.CompileAsLibrary = true;
        task.UseJavaScriptExportBoundary = true;
        task.Optimization = "None";

        Assert.True(task.Execute(), string.Join('\n', build.Errors.Select(error => error.Message)));

        Assert.Empty(build.Errors);
        Assert.True(File.Exists(task.CoreModulePath));
        Assert.Contains(build.Messages, message =>
            message.Importance == MessageImportance.High &&
            message.Message == "NetWasm: Compiling Worker.dll for wasm32...");
        Assert.Contains(build.Messages, message =>
            message.Importance == MessageImportance.High &&
            message.Message is { } text && text.StartsWith("NetWasm Compiler version ", StringComparison.Ordinal));
        Assert.Contains(build.Messages, message =>
            message.Importance == MessageImportance.High &&
            message.Message is { } text && text.StartsWith("NetWasm: Managed compilation completed.", StringComparison.Ordinal));
        Assert.Equal(
            $"NetWasm: Wrote {Path.GetFullPath(task.CoreModulePath)}.",
            build.Messages[^1].Message);
    }

    [Fact]
    public void ReportsNwcErrorsAsBuildErrors()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"netwasm-compile-task-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var build = new RecordingBuildEngine();
            var task = CreateTask(directory);
            task.BuildEngine = build;
            task.CompilerPath = Nwc;
            task.NoLogo = true;
            task.InputAssemblyPath = Path.Combine(directory, "Missing.dll");

            Assert.False(task.Execute());

            var error = Assert.Single(build.Errors);
            Assert.Equal("NW1014", error.Code);
            Assert.Contains("Missing.dll", error.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(build.Messages, message =>
                message.Message is { } text && text.StartsWith("NetWasm: Wrote", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static NetWasmCompileTask CreateTask(string? directory = null)
    {
        string PathTo(string name) => directory is null ? name : Path.Combine(directory, name);
        return new()
        {
            CompilerPath = "nwc",
            InputAssemblyPath = "App.dll",
            CoreModulePath = PathTo("app.wasm"),
            RuntimeLayoutPath = PathTo("layout.json"),
            InteropManifestPath = PathTo("interop.json"),
            CompilerMetadataPath = PathTo("metadata.json"),
            NativeCallbackObjectPath = PathTo("app.callbacks.o"),
            IntermediateDirectory = directory is null ? "obj/netwasm/" : Path.Combine(directory, "obj"),
            WasmToolsNodePath = "node",
            WasmToolsCommandPath = "run.mjs",
            WasmToolsModulePath = "wasm-tools.wasm",
        };
    }

    private static string[] Lines(string text) =>
        text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

    private sealed class RecordingBuildEngine : IBuildEngine
    {
        public List<BuildErrorEventArgs> Errors { get; } = [];
        public List<BuildMessageEventArgs> Messages { get; } = [];
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

        public void LogMessageEvent(BuildMessageEventArgs e) => Messages.Add(e);

        public void LogWarningEvent(BuildWarningEventArgs e)
        {
        }
    }
}
