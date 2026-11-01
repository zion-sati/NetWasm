using System.Collections;
using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.Build.Framework;
using NetWasm.Hosting.Build.Execution;
using NetWasm.Hosting.Build.MsBuild;

namespace NetWasm.Hosting.Build.Tests;

// Issue #140: dotnet run on a browser project publishes the site with the
// run's global properties before serving it. dotnet run computes its run
// command inside the .NET CLI, where the compiler's task hosts cannot start,
// so the development server runs the publish in a process of its own.
public sealed class BrowserRunStageWriterTests
{
    [Fact]
    public void WritesAPublishOfTheProjectWithTheRunsGlobalProperties()
    {
        using var directory = TemporaryDirectory.Create();
        var site = Path.Combine(directory.Path, "run-site");
        var output = Path.Combine(directory.Path, "stage", "run-stage.json");

        new BrowserRunStageWriter().Write(new(
            Path.Combine(directory.Path, "dotnet"),
            Path.Combine(directory.Path, "App.csproj"),
            site + Path.DirectorySeparatorChar,
            "wwwroot/app",
            ImmutableSortedDictionary.CreateRange(StringComparer.OrdinalIgnoreCase, new Dictionary<string, string>
            {
                ["Configuration"] = "Release",
                ["Defines"] = "A;B,C 100%",
                ["PublishDir"] = "ignored/",
                ["nobuild"] = "false",
            }),
            output));

        using var stage = JsonDocument.Parse(File.ReadAllText(output));
        Assert.Equal(Path.Combine(directory.Path, "dotnet"), stage.RootElement.GetProperty("command").GetString());
        Assert.Equal<string>(
        [
            "msbuild", Path.Combine(directory.Path, "App.csproj"), "-t:Publish", "-nologo", "-v:q",
            "-p:Configuration=Release",
            "-p:Defines=A%3BB%2CC 100%25",
            "-p:NoBuild=true",
            $"-p:PublishDir={site}{Path.DirectorySeparatorChar}",
            $"-p:NetWasmBrowserPublishDir={Path.Combine(site, "wwwroot/app")}",
            "-p:_NetWasmRunStaging=true",
        ], stage.RootElement.GetProperty("arguments").EnumerateArray().Select(argument => argument.GetString()!));
    }

    [Fact]
    public void RejectsRelativePathsAndABrowserDirectoryOutsideTheSite()
    {
        using var directory = TemporaryDirectory.Create();
        var valid = new BrowserRunStageRequest(
            Path.Combine(directory.Path, "dotnet"), Path.Combine(directory.Path, "App.csproj"),
            Path.Combine(directory.Path, "site"), "browser", ImmutableSortedDictionary<string, string>.Empty,
            Path.Combine(directory.Path, "stage.json"));
        var writer = new BrowserRunStageWriter();
        foreach (var request in new[]
        {
            valid with { DotNetPath = "dotnet" },
            valid with { ProjectPath = "App.csproj" },
            valid with { SiteDirectory = "site" },
            valid with { OutputPath = "stage.json" },
            valid with { BrowserDirectory = "../browser" },
            valid with { BrowserDirectory = Path.Combine(directory.Path, "browser") },
        })
        {
            Assert.Throws<ArgumentException>(() => writer.Write(request));
        }
        Assert.Throws<ArgumentNullException>(() => writer.Write(null!));
        Assert.False(File.Exists(valid.OutputPath));
    }

    [Fact]
    public void TaskPassesTheGlobalPropertiesMsBuildReportsAndNamesFailures()
    {
        using var directory = TemporaryDirectory.Create();
        var recorded = new List<BrowserRunStageRequest>();
        var engine = new GlobalPropertiesBuildEngine(new Dictionary<string, string> { ["Configuration"] = "Debug" });
        var task = new NetWasmWriteBrowserRunStageTask(new RecordingWriter(recorded.Add))
        {
            BuildEngine = engine,
            DotNetPath = Path.Combine(directory.Path, "dotnet"),
            ProjectPath = Path.Combine(directory.Path, "App.csproj"),
            SiteDirectory = Path.Combine(directory.Path, "site"),
            BrowserDirectory = "browser",
            OutputPath = Path.Combine(directory.Path, "stage.json"),
        };

        Assert.True(task.Execute());
        var request = Assert.Single(recorded);
        Assert.Equal("Debug", request.GlobalProperties["configuration"]);
        Assert.Equal(task.SiteDirectory, request.SiteDirectory);
        Assert.Equal("browser", request.BrowserDirectory);

        var failing = new NetWasmWriteBrowserRunStageTask(new RecordingWriter(_ => throw new ArgumentException("bad stage")))
        {
            BuildEngine = engine,
            DotNetPath = "d",
            ProjectPath = "p",
            SiteDirectory = "s",
            BrowserDirectory = "b",
            OutputPath = "o",
        };
        Assert.False(failing.Execute());
        Assert.Equal("NWSDK094: bad stage", engine.Errors.Single());

        var older = new RecordingBuildEngine();
        var unsupported = new NetWasmWriteBrowserRunStageTask
        {
            BuildEngine = older,
            DotNetPath = "d",
            ProjectPath = "p",
            SiteDirectory = "s",
            BrowserDirectory = "b",
            OutputPath = "o",
        };
        Assert.False(unsupported.Execute());
        Assert.StartsWith("NWSDK094: MSBuild does not report", Assert.Single(older.Errors), StringComparison.Ordinal);
        Assert.Throws<ArgumentNullException>(() => new NetWasmWriteBrowserRunStageTask(null!));
    }

    private sealed class RecordingWriter(Action<BrowserRunStageRequest> write) : IBrowserRunStageWriter
    {
        public void Write(BrowserRunStageRequest request) => write(request);
    }

    private class RecordingBuildEngine : IBuildEngine
    {
        public List<string> Errors { get; } = [];
        public bool ContinueOnError => false;
        public int LineNumberOfTaskNode => 0;
        public int ColumnNumberOfTaskNode => 0;
        public string ProjectFileOfTaskNode => string.Empty;
        public bool BuildProjectFile(string projectFileName, string[] targetNames, IDictionary globalProperties, IDictionary targetOutputs) =>
            throw new NotSupportedException();
        public void LogCustomEvent(CustomBuildEventArgs e) { }
        public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e.Message ?? string.Empty);
        public void LogMessageEvent(BuildMessageEventArgs e) { }
        public void LogWarningEvent(BuildWarningEventArgs e) { }
    }

    private sealed class GlobalPropertiesBuildEngine(IReadOnlyDictionary<string, string> properties)
        : RecordingBuildEngine, IBuildEngine6
    {
        public IReadOnlyDictionary<string, string> GetGlobalProperties() => properties;
        public bool IsRunningMultipleNodes => false;
        public bool BuildProjectFile(string projectFileName, string[] targetNames, IDictionary globalProperties,
            IDictionary targetOutputs, string toolsVersion) => throw new NotSupportedException();
        public bool BuildProjectFilesInParallel(string[] projectFileNames, string[] targetNames, IDictionary[] globalProperties,
            IDictionary[] targetOutputsPerProject, string[] toolsVersion, bool useResultsCache, bool unloadProjectsOnCompletion) =>
            throw new NotSupportedException();
        public BuildEngineResult BuildProjectFilesInParallel(string[] projectFileNames, string[] targetNames,
            IDictionary[] globalProperties, IList<string>[] removeGlobalProperties, string[] toolsVersion, bool returnTargetOutputs) =>
            throw new NotSupportedException();
        public void Yield() => throw new NotSupportedException();
        public void Reacquire() => throw new NotSupportedException();
        public void RegisterTaskObject(object key, object obj, RegisteredTaskObjectLifetime lifetime, bool allowEarlyCollection) =>
            throw new NotSupportedException();
        public object GetRegisteredTaskObject(object key, RegisteredTaskObjectLifetime lifetime) => throw new NotSupportedException();
        public object UnregisterTaskObject(object key, RegisteredTaskObjectLifetime lifetime) => throw new NotSupportedException();
        public void LogTelemetry(string eventName, IDictionary<string, string> properties) => throw new NotSupportedException();
    }
}
