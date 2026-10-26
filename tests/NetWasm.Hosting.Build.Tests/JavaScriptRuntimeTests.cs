using System.Text.Json;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using NetWasm.Hosting.Build.JavaScript;
using NetWasm.Hosting.Build.MsBuild;

namespace NetWasm.Hosting.Build.Tests;

// Issue #128: a page and its workers import one shared runtime, so they
// download it once instead of each bundling its own copy.
public sealed class JavaScriptRuntimeTests
{
    private const string Manifest = """
        {"schemaVersion":1,"modules":{
          "page":{"file":"netwasm.runtime.page.a1.mjs","files":["netwasm.runtime.page.a1.mjs","netwasm.runtime.s1.mjs"]},
          "worker":{"file":"netwasm.runtime.worker.b2.mjs","files":["netwasm.runtime.s1.mjs","netwasm.runtime.worker.b2.mjs"]},
          "random":{"file":"netwasm.runtime.random.c3.mjs","files":["netwasm.runtime.random.c3.mjs","netwasm.runtime.s2.mjs"]}}}
        """;

    [Fact]
    public void WritesTheSameFacadeSetForEveryProject()
    {
        using var directory = TemporaryDirectory.Create();
        var facades = Path.Combine(directory.Path, "facades");
        Directory.CreateDirectory(facades);
        File.WriteAllText(Path.Combine(facades, "stale.mjs"), "export {};\n");
        File.WriteAllText(Path.Combine(facades, "notes.txt"), "kept");

        JavaScriptRuntime.WriteFacades(facades, Path.Combine(directory.Path, "hosting"), Path.Combine(directory.Path, "shim"));

        Assert.Equal(
            ["clocks", "filesystem", "http", "manifest-validator", "page", "random", "sockets", "worker", "worker-client"],
            Directory.EnumerateFiles(facades, "*.mjs").Select(Path.GetFileNameWithoutExtension).Order(StringComparer.Ordinal));
        Assert.True(File.Exists(Path.Combine(facades, "notes.txt")));
        var page = File.ReadAllText(Path.Combine(facades, "page.mjs"));
        Assert.Contains("export { createPageSession } from \"file://", page, StringComparison.Ordinal);
        Assert.Contains("/hosting/page-session-root.mjs\";", page, StringComparison.Ordinal);
        Assert.Contains("export * as cli from \"file://", page, StringComparison.Ordinal);
        Assert.Contains("/shim/dist/browser/io.js\";", page, StringComparison.Ordinal);
        Assert.DoesNotContain("random", page, StringComparison.Ordinal);
        var filesystem = File.ReadAllText(Path.Combine(facades, "filesystem.mjs"));
        Assert.Contains("export { createPreview2FilesystemScope } from", filesystem, StringComparison.Ordinal);
        Assert.Contains("export * as filesystem from", filesystem, StringComparison.Ordinal);
        Assert.Equal("netwasm:runtime/worker-client", JavaScriptRuntime.Specifier(JavaScriptRuntime.WorkerClient));
        Assert.Equal("page", JavaScriptRuntime.FamilyModule("io", JavaScriptRuntime.Page));
        Assert.Equal("clocks", JavaScriptRuntime.FamilyModule("clocks", JavaScriptRuntime.Worker));
    }

    [Fact]
    public void ReadsTheRuntimeManifestAndTheModulesAnEntryImports()
    {
        using var directory = TemporaryDirectory.Create();
        var path = Path.Combine(directory.Path, "runtime.json");
        File.WriteAllText(path, Manifest);

        var modules = JavaScriptRuntimeManifest.Read(path);

        Assert.Equal("netwasm.runtime.page.a1.mjs", modules["page"].File);
        Assert.Equal(["netwasm.runtime.s1.mjs", "netwasm.runtime.worker.b2.mjs"], modules["worker"].Files.ToArray());
        Assert.Equal(["page", "random"], JavaScriptRuntimeManifest.FindImports(
            "import { a } from \"netwasm:runtime/random\";\nimport { b, c } from \"netwasm:runtime/page\";\n"
            + "import { d } from \"netwasm:runtime/page\";\nconst text = \"netwasm:runtime/\";\n").ToArray());
        foreach (var invalid in new[]
                 {
                     "[]",
                     "{\"schemaVersion\":2,\"modules\":{}}",
                     "{\"schemaVersion\":1}",
                     "{\"schemaVersion\":1,\"modules\":{\"Page\":{\"file\":\"netwasm.runtime.a.mjs\",\"files\":[\"netwasm.runtime.a.mjs\"]}}}",
                     "{\"schemaVersion\":1,\"modules\":{\"page\":{\"file\":\"netwasm.runtime.a.mjs\",\"files\":[]}}}",
                     "{\"schemaVersion\":1,\"modules\":{\"page\":{\"file\":\"../a.mjs\",\"files\":[\"../a.mjs\"]}}}",
                     "{\"schemaVersion\":1,\"modules\":{\"page\":{\"file\":\"netwasm.runtime.a.mjs\",\"files\":[1]}}}",
                 })
        {
            File.WriteAllText(path, invalid);
            Assert.Throws<InvalidDataException>(() => JavaScriptRuntimeManifest.Read(path));
        }
    }

    [Theory]
    [InlineData("./", "./netwasm.runtime.page.a1.mjs")]
    [InlineData("../../", "../../netwasm.runtime.page.a1.mjs")]
    public void BundlesAnEntryAgainstTheRuntimeAndReportsTheFilesItLoads(string prefix, string url)
    {
        using var directory = TemporaryDirectory.Create();
        var runtime = Path.Combine(directory.Path, "runtime", "runtime.json");
        Directory.CreateDirectory(Path.GetDirectoryName(runtime)!);
        File.WriteAllText(runtime, Manifest);
        var entry = Path.Combine(directory.Path, "page-entry.mjs");
        File.WriteAllText(entry,
            "import { createPageSession } from \"netwasm:runtime/page\";\nimport { random } from \"netwasm:runtime/random\";\n");
        var bundler = new RecordingBundler();
        var task = new NetWasmBundleJavaScriptTask(bundler)
        {
            BuildEngine = new RecordingBuildEngine(),
            NodePath = Path.Combine(directory.Path, "node"),
            CommandPath = Path.Combine(directory.Path, "bundle.mjs"),
            EntryPath = entry,
            OutputPath = Path.Combine(directory.Path, "netwasm.page.mjs"),
            Platform = "browser",
            RuntimeManifestPath = runtime,
            RuntimeUrlPrefix = prefix,
        };

        Assert.True(task.Execute());

        var request = Assert.Single(bundler.Bundles);
        Assert.Equal(entry + ".runtime-imports.json", request.RuntimeImportsPath);
        var imports = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(request.RuntimeImportsPath!))!;
        Assert.Equal(url, imports["netwasm:runtime/page"]);
        Assert.Equal(2, imports.Count);
        Assert.Equal(
            ["netwasm.runtime.page.a1.mjs", "netwasm.runtime.random.c3.mjs", "netwasm.runtime.s1.mjs", "netwasm.runtime.s2.mjs"],
            task.RuntimeFiles.Select(item => item.GetMetadata("RelativePath")));
        Assert.All(task.RuntimeFiles, item => Assert.Equal(
            Path.Combine(directory.Path, "runtime", item.GetMetadata("RelativePath")), item.ItemSpec));
    }

    [Fact]
    public void BundlesWithoutTheRuntimeOrRejectsAMismatch()
    {
        using var directory = TemporaryDirectory.Create();
        var runtime = Path.Combine(directory.Path, "runtime.json");
        File.WriteAllText(runtime, Manifest);
        var entry = Path.Combine(directory.Path, "entry.mjs");
        File.WriteAllText(entry, "import { client } from \"netwasm:runtime/worker-client\";\n");
        NetWasmBundleJavaScriptTask Task(RecordingBundler bundler, RecordingBuildEngine build) => new(bundler)
        {
            BuildEngine = build,
            NodePath = Path.Combine(directory.Path, "node"),
            CommandPath = Path.Combine(directory.Path, "bundle.mjs"),
            EntryPath = entry,
            OutputPath = Path.Combine(directory.Path, "out.mjs"),
            Platform = "node",
        };

        var plain = new RecordingBundler();
        var unmapped = Task(plain, new RecordingBuildEngine());
        Assert.True(unmapped.Execute());
        Assert.Null(Assert.Single(plain.Bundles).RuntimeImportsPath);
        Assert.Empty(unmapped.RuntimeFiles);

        foreach (var (manifest, prefix) in new[] { (runtime, "./"), (runtime, "../x/"), (runtime, ""), (runtime, "..") })
        {
            var bundler = new RecordingBundler();
            var build = new RecordingBuildEngine();
            var task = Task(bundler, build);
            task.RuntimeManifestPath = manifest;
            task.RuntimeUrlPrefix = prefix;
            Assert.False(task.Execute());
            Assert.Empty(bundler.Bundles);
            Assert.Contains("NWSDK041", Assert.Single(build.Errors), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void BuildsTheRuntimeFromFreshFacadesIntoAFreshDirectory()
    {
        using var directory = TemporaryDirectory.Create();
        var output = Path.Combine(directory.Path, "javascript-runtime");
        var stale = Path.Combine(output, "minified", "netwasm.runtime.old.mjs");
        Directory.CreateDirectory(Path.GetDirectoryName(stale)!);
        File.WriteAllText(stale, "old");
        var bundler = new RecordingBundler
        {
            OnRuntime = request =>
            {
                Assert.False(File.Exists(stale));
                Assert.True(File.Exists(Path.Combine(request.FacadeDirectory, "page.mjs")));
                Directory.CreateDirectory(request.OutputDirectory);
                File.WriteAllText(Path.Combine(request.OutputDirectory, "runtime.json"), Manifest);
            },
        };
        var task = new NetWasmBuildJavaScriptRuntimeTask(bundler)
        {
            BuildEngine = new RecordingBuildEngine(),
            NodePath = Path.Combine(directory.Path, "node"),
            CommandPath = Path.Combine(directory.Path, "bundle.mjs"),
            HostingJavaScriptRoot = Path.Combine(directory.Path, "hosting"),
            Preview2ShimRoot = Path.Combine(directory.Path, "shim"),
            OutputDirectory = output,
            Minify = true,
        };

        Assert.True(task.Execute());

        var request = Assert.Single(bundler.Runtimes);
        Assert.Equal(Path.Combine(output, "facades"), request.FacadeDirectory);
        Assert.Equal(Path.Combine(output, "minified"), request.OutputDirectory);
        Assert.True(request.Minify);
        Assert.Equal(Path.Combine(output, "minified", "runtime.json"), task.ManifestPath);

        var build = new RecordingBuildEngine();
        var failing = new NetWasmBuildJavaScriptRuntimeTask(new RecordingBundler { OnRuntime = _ => { } })
        {
            BuildEngine = build,
            NodePath = task.NodePath,
            CommandPath = task.CommandPath,
            HostingJavaScriptRoot = task.HostingJavaScriptRoot,
            Preview2ShimRoot = task.Preview2ShimRoot,
            OutputDirectory = output,
        };
        Assert.False(failing.Execute());
        Assert.Equal(string.Empty, failing.ManifestPath);
        Assert.Contains("NWSDK091", Assert.Single(build.Errors), StringComparison.Ordinal);
        Assert.Throws<ArgumentNullException>(() => new NetWasmBuildJavaScriptRuntimeTask(null!));
    }

    private sealed class RecordingBundler : IJavaScriptBundler
    {
        public List<JavaScriptBundleRequest> Bundles { get; } = [];
        public List<JavaScriptRuntimeBundleRequest> Runtimes { get; } = [];
        public Action<JavaScriptRuntimeBundleRequest> OnRuntime { get; init; } = _ => { };

        public void Bundle(JavaScriptBundleRequest request) => Bundles.Add(request);

        public void BundleRuntime(JavaScriptRuntimeBundleRequest request)
        {
            Runtimes.Add(request);
            OnRuntime(request);
        }
    }

    private sealed class RecordingBuildEngine : IBuildEngine
    {
        public List<string> Errors { get; } = [];
        public bool ContinueOnError => false;
        public int LineNumberOfTaskNode => 0;
        public int ColumnNumberOfTaskNode => 0;
        public string ProjectFileOfTaskNode => string.Empty;
        public bool BuildProjectFile(string projectFileName, string[] targetNames,
            System.Collections.IDictionary globalProperties, System.Collections.IDictionary targetOutputs) => true;
        public void LogCustomEvent(CustomBuildEventArgs e) { }
        public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e.Message ?? string.Empty);
        public void LogMessageEvent(BuildMessageEventArgs e) { }
        public void LogWarningEvent(BuildWarningEventArgs e) { }
    }
}
