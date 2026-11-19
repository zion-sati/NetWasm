// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using NetWasm.Hosting.Build.JavaScript;
using NetWasm.Hosting.Build.MsBuild;

namespace NetWasm.Hosting.Build.Tests;

public sealed class NetWasmWritePageEntriesTaskTests
{
    [Fact]
    public void HostTaskBundlesUsedModulesAndLeavesTheRestToOpen()
    {
        var writer = new RecordingWriter();
        var task = new NetWasmWritePageHostEntryTask(writer)
        {
            HostingJavaScriptRoot = Path.GetFullPath("hosting"),
            OutputPath = Path.GetFullPath("page-entry.mjs"),
            Imports =
            [
                Item("report", ("Module", "app.host"), ("Name", "report")),
                Item("lookup", ("Module", "app.runtime"), ("Name", "lookup")),
            ],
            Modules = [Item("host.mjs", ("Module", "app.host"))],
            Arguments = [new TaskItem("argument")],
            EnvironmentVariables = [Item("MODE", ("Value", "test"))],
            Clocks = [new TaskItem("wall")],
            Network = "denyAll",
            Randomness = true,
        };

        Assert.True(Assert.IsAssignableFrom<ITask>(task).Execute());

        var request = Assert.IsType<PageHostEntryRequest>(writer.Host);
        Assert.Equal(new BrowserJavaScriptModule("app.host", Path.GetFullPath("host.mjs")), Assert.Single(request.Modules));
        Assert.Equal(["argument"], request.Arguments.ToArray());
        Assert.Equal(new WorkerEnvironmentVariable("MODE", "test"), Assert.Single(request.Environment));
        Assert.Equal(["wall"], request.Clocks.ToArray());
        Assert.Equal("denyAll", request.Network);
        Assert.True(request.Randomness);
        Assert.Equal(PageHostPlatform.Browser, request.Platform);
    }

    // Issue #178: the same host, for Node.js code to import.
    [Theory]
    [InlineData("node", true)]
    [InlineData("deno", false)]
    public void HostTaskSelectsItsPlatform(string platform, bool valid)
    {
        var writer = new RecordingWriter();
        var task = new NetWasmWritePageHostEntryTask(writer)
        {
            BuildEngine = new RecordingBuildEngine(),
            HostingJavaScriptRoot = Path.GetFullPath("hosting"),
            OutputPath = Path.GetFullPath("node-entry.mjs"),
            Network = "allowAll",
            Platform = platform,
        };

        Assert.Equal(valid, task.Execute());

        Assert.Equal(valid ? PageHostPlatform.Node : null, writer.Host?.Platform);
    }

    // The final module calls an import module's functions, so bundling it is a use.
    [Fact]
    public void HostTaskBundlesImportModules()
    {
        var writer = new RecordingWriter();
        var task = new NetWasmWritePageHostEntryTask(writer)
        {
            HostingJavaScriptRoot = Path.GetFullPath("hosting"),
            OutputPath = Path.GetFullPath("page-entry.mjs"),
            ImportModules = [new TaskItem("host_ui")],
            Modules = [Item("host-ui.mjs", ("Module", "host_ui"))],
            Network = "denyAll",
        };

        Assert.True(task.Execute());

        Assert.Equal(new BrowserJavaScriptModule("host_ui", Path.GetFullPath("host-ui.mjs")),
            Assert.Single(Assert.IsType<PageHostEntryRequest>(writer.Host).Modules));
    }

    // Issue #128: a module for a feature trimming removed is left out rather than
    // failing the build, so a project pays only for the modules it uses.
    [Fact]
    public void HostTaskLeavesOutUnusedModules()
    {
        var writer = new RecordingWriter();
        var task = new NetWasmWritePageHostEntryTask(writer)
        {
            HostingJavaScriptRoot = Path.GetFullPath("hosting"),
            OutputPath = Path.GetFullPath("page-entry.mjs"),
            Imports = [Item("report", ("Module", "app.host"), ("Name", "report"))],
            Modules =
            [
                Item("extra.mjs", ("Module", "app.extra")),
                Item("host.mjs", ("Module", "app.host")),
                Item("client.mjs", ("Module", "netwasm:worker/Unused"), ("NetWasmGeneratedWorkerClient", "true")),
            ],
            Network = "denyAll",
        };

        Assert.True(task.Execute());

        Assert.Equal(new BrowserJavaScriptModule("app.host", Path.GetFullPath("host.mjs")),
            Assert.Single(Assert.IsType<PageHostEntryRequest>(writer.Host).Modules));
    }

    [Fact]
    public void HostTaskRejectsUnnamedModules()
    {
        var build = new RecordingBuildEngine();
        var task = new NetWasmWritePageHostEntryTask(new RecordingWriter())
        {
            BuildEngine = build,
            HostingJavaScriptRoot = Path.GetFullPath("hosting"),
            OutputPath = Path.GetFullPath("page-entry.mjs"),
            Imports = [Item("report", ("Module", "app.host"), ("Name", "report"))],
            Modules = [new TaskItem("unnamed.mjs")],
            Network = "denyAll",
        };

        Assert.False(task.Execute());
        Assert.Contains("NWSDK077", Assert.Single(build.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public void ModuleTaskPassesTheDeploymentIdentityAndExports()
    {
        var writer = new RecordingWriter();
        var task = new NetWasmWritePageModuleTask(writer)
        {
            PageFileName = "netwasm.page.mjs",
            BuildFingerprint = new string('1', 64),
            ManifestSha256 = new string('2', 64),
            OutputPath = Path.GetFullPath("App.mjs"),
            DeclarationPath = Path.GetFullPath("App.d.mts"),
            Exports =
            [
                Item("Add", ("Name", "Add"), ("Parameters", "[\"i32\",\"i32\"]"), ("Result", "i32")),
                Item("Fetch", ("Name", "Fetch"), ("Parameters", "[]"), ("Result", "string"), ("AsyncReturn", "task")),
            ],
        };

        Assert.True(task.Execute());

        var request = Assert.IsType<PageModuleRequest>(writer.Module);
        Assert.Equal("netwasm.page.mjs", request.PageFileName);
        Assert.Equal(Path.GetFullPath("App.d.mts"), request.DeclarationPath);
        Assert.Equal(new WorkerJavaScriptExport("Add", ["i32", "i32"], "i32", null).Name, request.Exports[0].Name);
        Assert.Equal(["i32", "i32"], request.Exports[0].Parameters.ToArray());
        Assert.Null(request.Exports[0].AsyncReturn);
        Assert.Equal("task", request.Exports[1].AsyncReturn);
        Assert.False(request.FileMounts);

        // A page that links wasi:filesystem re-exports its file mounts.
        task.RequiredImportModules = [Item("wasi:cli/stdout@0.3.2"), Item("wasi:filesystem/types@0.3.2")];
        Assert.True(task.Execute());
        Assert.True(Assert.IsType<PageModuleRequest>(writer.Module).FileMounts);
        // Without deployment artifacts (Node.js code) the module imports the bundle statically.
        Assert.Null(Assert.IsType<PageModuleRequest>(writer.Module).StartupLoad);
    }

    // A browser page prefetches the artifacts its runtime fetches at every startup, and preloads the runtime modules.
    [Fact]
    public void ModuleTaskPassesThePageStartupDownloads()
    {
        var writer = new RecordingWriter();
        var task = new NetWasmWritePageModuleTask(writer)
        {
            PageFileName = "netwasm.page.mjs",
            BuildFingerprint = new string('1', 64),
            ManifestSha256 = new string('2', 64),
            OutputPath = Path.GetFullPath("App.mjs"),
            DeclarationPath = Path.GetFullPath("App.d.mts"),
            Exports = [Item("Add", ("Name", "Add"), ("Parameters", "[]"), ("Result", "i32"))],
            RuntimeModules = [Item("./netwasm.runtime.page.a.mjs"), Item("./netwasm.runtime.b.mjs")],
            DeploymentArtifacts =
            [
                Item("/obj/App.wasm", ("RelativePath", "App.wasm"), ("Role", "application")),
                Item("/obj/zones", ("RelativePath", "tz\\zones.bin"), ("Role", "timezone-data")),
                Item("/obj/netwasm.page.mjs", ("RelativePath", "netwasm.page.mjs"), ("Role", "hosting-javascript")),
                Item("/obj/App.raw-adapter.mjs", ("RelativePath", "App.raw-adapter.mjs"), ("Role", "raw-adapter")),
                Item("/obj/runtime-layout.json", ("RelativePath", "runtime-layout.json"), ("Role", "runtime-layout")),
                Item("/obj/interop.json", ("RelativePath", "interop.json"), ("Role", "interop-manifest")),
                Item("/obj/App.wasm.type-symbols", ("RelativePath", "App.wasm.type-symbols"), ("Role", "type-symbols")),
                Item("/obj/m", ("RelativePath", "sym\\App.wasm.method-symbols"), ("Role", "method-symbols")),
            ],
        };

        Assert.True(task.Execute());

        var load = Assert.IsType<PageStartupLoad>(Assert.IsType<PageModuleRequest>(writer.Module).StartupLoad);
        Assert.Equal(["./netwasm.runtime.page.a.mjs", "./netwasm.runtime.b.mjs"], load.RuntimeModules.ToArray());
        Assert.Equal(["App.wasm", "App.raw-adapter.mjs", "runtime-layout.json", "interop.json", "App.wasm.type-symbols",
            "sym/App.wasm.method-symbols"], load.DeploymentFiles.ToArray());
    }

    [Theory]
    [InlineData("Role")]
    [InlineData("RelativePath")]
    public void ModuleTaskRejectsDeploymentArtifactsWithoutTheirMetadata(string missing)
    {
        var build = new RecordingBuildEngine();
        var metadata = new[] { ("RelativePath", "App.wasm"), ("Role", "application") }
            .Where(pair => pair.Item1 != missing).ToArray();
        var task = new NetWasmWritePageModuleTask(new RecordingWriter())
        {
            BuildEngine = build,
            PageFileName = "netwasm.page.mjs",
            BuildFingerprint = new string('1', 64),
            ManifestSha256 = new string('2', 64),
            OutputPath = Path.GetFullPath("App.mjs"),
            DeclarationPath = Path.GetFullPath("App.d.mts"),
            Exports = [Item("Add", ("Name", "Add"), ("Parameters", "[]"), ("Result", "i32"))],
            DeploymentArtifacts = [Item("/obj/App.wasm", metadata)],
        };

        Assert.False(task.Execute());
        Assert.Contains("NWSDK078", Assert.Single(build.Errors), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("[null]")]
    public void ModuleTaskRejectsMalformedSignatures(string parameters)
    {
        var build = new RecordingBuildEngine();
        var task = new NetWasmWritePageModuleTask(new RecordingWriter())
        {
            BuildEngine = build,
            PageFileName = "netwasm.page.mjs",
            BuildFingerprint = new string('1', 64),
            ManifestSha256 = new string('2', 64),
            OutputPath = Path.GetFullPath("App.mjs"),
            DeclarationPath = Path.GetFullPath("App.d.mts"),
            Exports = [Item("Add", ("Name", "Add"), ("Parameters", parameters), ("Result", "i32"))],
        };

        Assert.False(task.Execute());
        Assert.Contains("NWSDK078", Assert.Single(build.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public void TasksRequireAWriter()
    {
        Assert.Throws<ArgumentNullException>(() => new NetWasmWritePageHostEntryTask(null!));
        Assert.Throws<ArgumentNullException>(() => new NetWasmWritePageModuleTask(null!));
        Assert.IsAssignableFrom<ITask>(new NetWasmWritePageHostEntryTask());
        Assert.IsAssignableFrom<ITask>(new NetWasmWritePageModuleTask());
    }

    [Fact]
    public void CommandRunModuleTaskPassesTheBuildConfigurationAndReportsFailures()
    {
        var writer = new RecordingRunWriter();
        var task = new NetWasmWriteCommandRunModuleTask(writer)
        {
            BundleFileName = "netwasm.browser.mjs",
            BuildFingerprint = new string('1', 64),
            ManifestSha256 = new string('2', 64),
            OutputPath = "App.mjs",
            DeclarationPath = "App.d.mts",
            Arguments = [new TaskItem("App"), new TaskItem("--verbose")],
            EnvironmentVariables = [Item("MODE", ("Value", "page"))],
            Clocks = [new TaskItem("wall")],
            Network = "allowAll",
            Randomness = true,
        };

        Assert.True(Assert.IsAssignableFrom<ITask>(task).Execute());

        var request = Assert.IsType<CommandRunModuleRequest>(writer.Request);
        Assert.Equal(Path.GetFullPath("App.mjs"), request.OutputPath);
        Assert.Equal(Path.GetFullPath("App.d.mts"), request.DeclarationPath);
        Assert.Equal(["App", "--verbose"], request.Arguments.ToArray());
        Assert.Equal(new WorkerEnvironmentVariable("MODE", "page"), Assert.Single(request.Environment));
        Assert.Equal(["wall"], request.Clocks.ToArray());
        Assert.Equal("allowAll", request.Network);
        Assert.True(request.Randomness);

        var build = new RecordingBuildEngine();
        var failing = new NetWasmWriteCommandRunModuleTask
        {
            BuildEngine = build,
            BundleFileName = "netwasm.browser.mjs",
            BuildFingerprint = "not-a-digest",
            ManifestSha256 = new string('2', 64),
            OutputPath = "App.mjs",
            DeclarationPath = "App.d.mts",
            Network = "allowAll",
        };
        Assert.False(failing.Execute());
        Assert.StartsWith("NWSDK082: ", Assert.Single(build.Errors), StringComparison.Ordinal);
        Assert.Throws<ArgumentNullException>(() => new NetWasmWriteCommandRunModuleTask(null!));
    }

    private static TaskItem Item(string include, params (string Name, string Value)[] metadata)
    {
        var item = new TaskItem(include);
        foreach (var (name, value) in metadata) item.SetMetadata(name, value);
        return item;
    }

    private sealed class RecordingWriter : IPageEntryWriter
    {
        public PageHostEntryRequest? Host { get; private set; }
        public PageModuleRequest? Module { get; private set; }
        public void WriteHost(PageHostEntryRequest request) => Host = request;
        public void WriteModule(PageModuleRequest request) => Module = request;
    }

    private sealed class RecordingRunWriter : ICommandRunModuleWriter
    {
        public CommandRunModuleRequest? Request { get; private set; }
        public void Write(CommandRunModuleRequest request) => Request = request;
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
