using System.Text.Json;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using NetWasm.Hosting.Build.Execution;
using NetWasm.Hosting.Build.MsBuild;

namespace NetWasm.Hosting.Build.Tests;

// A browser project's site is every file it serves, each declared during the build with the path
// it is served at. Publish copies the site files with the project's other files; dotnet run serves
// them in place, from the map the build writes.
public sealed class SiteFilesTests
{
    [Fact]
    public void ADeclaredFileReplacesWhatPublishCopiedAndAFallbackOnlyFillsAGap()
    {
        var publication = new SiteFileResolver().Resolve(
            [new("content/index.html", "index.html"), new("content/app.css", "css\\app.css"), new("content/a.txt", "a.txt")],
            [
                new("obj/app.css", "css/app.css"),
                new("obj/other.css", "css\\app.css"),
                new("obj/index.html", "index.html", Fallback: true),
                new("obj/page.html", "page.html", Fallback: true),
                new("obj/a.txt", "a.txt", Fallback: true),
                new("obj/a2.txt", "a.txt"),
                new("obj/page2.html", "page.html", Fallback: true),
            ]);

        Assert.Equal([1, 2], publication.Replaced.Order());
        Assert.Equal([(0, "css/app.css"), (5, "a.txt"), (3, "page.html")], publication.Added);
    }

    [Fact]
    public void TheMapNamesEveryPathAUrlCanReachOnce()
    {
        using var directory = TemporaryDirectory.Create();
        var map = Path.Combine(directory.Path, "obj", "site.json");
        var source = Path.Combine(directory.Path, "b.js");

        new SiteMapWriter().Write(map,
        [
            new(source, "browser\\b.js"),
            new(Path.Combine(directory.Path, "a.html"), "index.html"),
            new(Path.Combine(directory.Path, "late.js"), "browser/b.js"),
            new(source, "../outside.js"),
            new(source, "nested/../escape.js"),
            new(source, "/rooted.js"),
            new(source, ""),
        ]);

        using var json = JsonDocument.Parse(File.ReadAllText(map));
        Assert.Equal(1, json.RootElement.GetProperty("version").GetInt32());
        Assert.Equal(
            [("browser/b.js", Path.Combine(directory.Path, "late.js")), ("index.html", Path.Combine(directory.Path, "a.html"))],
            json.RootElement.GetProperty("files").EnumerateObject().Select(file => (file.Name, file.Value.GetString()!)));
        Assert.Throws<ArgumentException>(() => new SiteMapWriter().Write("", []));
        Assert.Throws<ArgumentNullException>(() => new SiteMapWriter().Write(map, null!));
        Assert.Throws<ArgumentNullException>(() => new SiteFileResolver().Resolve(null!, []));
        Assert.Throws<ArgumentNullException>(() => new SiteFileResolver().Resolve([], null!));
    }

    [Fact]
    public void PublishCopiesTheSiteFilesAtTheirPaths()
    {
        var content = new TaskItem("content/index.html", new Dictionary<string, string> { ["RelativePath"] = "index.html" });
        var css = new TaskItem("content/app.css", new Dictionary<string, string> { ["RelativePath"] = "app.css" });
        var task = new NetWasmResolveSiteFilesTask
        {
            PublishFiles = [content, css],
            SiteFiles =
            [
                new TaskItem("obj/app.css", new Dictionary<string, string> { ["SitePath"] = "app.css", ["Role"] = "style" }),
                new TaskItem("obj/index.html", new Dictionary<string, string> { ["SitePath"] = "index.html", ["Fallback"] = "True" }),
            ],
        };

        Assert.True(task.Execute());

        Assert.Equal(["content/index.html", "obj/app.css"], task.FilesToPublish.Select(item => item.ItemSpec));
        var site = task.FilesToPublish[1];
        Assert.Equal("app.css", site.GetMetadata("RelativePath"));
        Assert.Equal("PreserveNewest", site.GetMetadata("CopyToPublishDirectory"));
        Assert.Equal("style", site.GetMetadata("Role"));
        Assert.Throws<ArgumentNullException>(() => new NetWasmResolveSiteFilesTask(null!));
    }

    [Fact]
    public void TheBuildWritesTheMapAndReportsWhenItCannot()
    {
        using var directory = TemporaryDirectory.Create();
        var map = Path.Combine(directory.Path, "site.json");
        var task = new NetWasmWriteSiteMapTask
        {
            Files = [new TaskItem(Path.Combine(directory.Path, "index.html"), new Dictionary<string, string> { ["RelativePath"] = "index.html" })],
            OutputPath = map,
        };

        Assert.True(task.Execute());
        Assert.Contains("index.html", File.ReadAllText(map), StringComparison.Ordinal);

        var engine = new RecordingBuildEngine();
        var failing = new NetWasmWriteSiteMapTask(new FailingWriter()) { BuildEngine = engine, OutputPath = map };
        Assert.False(failing.Execute());
        Assert.StartsWith("NWSDK094: the site map could not be written:", Assert.Single(engine.Errors), StringComparison.Ordinal);
        Assert.Throws<ArgumentNullException>(() => new NetWasmWriteSiteMapTask(null!));
    }

    private sealed class FailingWriter : ISiteMapWriter
    {
        public void Write(string outputPath, IReadOnlyList<PublishedFile> files) => throw new IOException("disk full");
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
        public bool BuildProjectFile(string projectFileName, string[] targetNames, System.Collections.IDictionary globalProperties,
            System.Collections.IDictionary targetOutputs) => true;
    }
}
