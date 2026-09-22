using System.Collections;
using System.Text;

using Microsoft.Build.Framework;

using NetWasm.Sdk.Pack.Archives;
using NetWasm.Sdk.Pack.Restore;

namespace NetWasm.Sdk.Pack.Tests.Restore;

public sealed class CanonicalRestoreGraphMsBuildTaskTests
{
    [Fact]
    public void DelegatesTheGraphAndProfileAndWritesOnlyTheCanonicalResult()
    {
        var streams = new MemoryStreams();
        var canonicalizer = new RecordingCanonicalizer();
        var task = Create(canonicalizer, streams);

        Assert.True(task.Execute());
        Assert.Equal("input", Encoding.UTF8.GetString(canonicalizer.Graph!));
        Assert.Equal("NetWasm,Version=v0.1", canonicalizer.Profiles!["netwasm0.1"]);
        Assert.Equal("output", Encoding.UTF8.GetString(streams.Output.ToArray()));
        Assert.Equal(["graph.json", "graph.json"], streams.Paths);
        Assert.Empty(((BuildEngine)task.BuildEngine).Errors);
    }

    [Theory]
    [InlineData("path")]
    [InlineData("alias")]
    [InlineData("canonical")]
    public void RejectsMissingPropertiesBeforeOpeningStreams(string missing)
    {
        var streams = new MemoryStreams();
        var canonicalizer = new RecordingCanonicalizer();
        var task = Create(canonicalizer, streams);
        if (missing == "path") task.GraphPath = "";
        if (missing == "alias") task.TargetFrameworkAlias = "";
        if (missing == "canonical") task.CanonicalTargetFramework = "";

        Assert.False(task.Execute());
        Assert.Empty(streams.Paths);
        Assert.Null(canonicalizer.Graph);
        Assert.Single(((BuildEngine)task.BuildEngine).Errors);
    }

    [Fact]
    public void DoesNotOpenOutputWhenCanonicalizationFails()
    {
        var streams = new MemoryStreams();
        var canonicalizer = new RecordingCanonicalizer { Fail = true };
        var task = Create(canonicalizer, streams);

        Assert.False(task.Execute());
        Assert.Equal(["graph.json"], streams.Paths);
        Assert.Empty(streams.Output.ToArray());
        Assert.Single(((BuildEngine)task.BuildEngine).Errors);
    }

    [Fact]
    public void RejectsMissingCollaborators()
    {
        var streams = new MemoryStreams();
        var canonicalizer = new RecordingCanonicalizer();
        Assert.Throws<ArgumentNullException>(() => new CanonicalRestoreGraphMsBuildTask(null!, streams, streams));
        Assert.Throws<ArgumentNullException>(() => new CanonicalRestoreGraphMsBuildTask(canonicalizer, null!, streams));
        Assert.Throws<ArgumentNullException>(() => new CanonicalRestoreGraphMsBuildTask(canonicalizer, streams, null!));
    }

    [Fact]
    public void DefaultAdapterPersistsACanonicalGraphThroughItsFileBoundary()
    {
        var path = Path.Combine(Path.GetTempPath(), $"netwasm-cli-graph-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """
                {"projects":{"App":{"restore":{},"frameworks":{"netwasm0.1":{}}}}}
                """);
            var task = new CanonicalRestoreGraphMsBuildTask
            {
                BuildEngine = new BuildEngine(),
                GraphPath = path,
                TargetFrameworkAlias = "netwasm0.1",
                CanonicalTargetFramework = "NetWasm,Version=v0.1"
            };

            Assert.True(task.Execute());
            Assert.Contains("NetWasm,Version=v0.1", File.ReadAllText(path), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static CanonicalRestoreGraphMsBuildTask Create(RecordingCanonicalizer canonicalizer, MemoryStreams streams) => new(canonicalizer, streams, streams)
    {
        BuildEngine = new BuildEngine(),
        GraphPath = "graph.json",
        TargetFrameworkAlias = "netwasm0.1",
        CanonicalTargetFramework = "NetWasm,Version=v0.1"
    };

    private sealed class RecordingCanonicalizer : IRestoreGraphCanonicalizer
    {
        public byte[]? Graph { get; private set; }
        public IReadOnlyDictionary<string, string>? Profiles { get; private set; }
        public bool Fail { get; init; }

        public byte[] Canonicalize(byte[] graph, IReadOnlyDictionary<string, string> profiles)
        {
            Graph = graph;
            Profiles = profiles;
            if (Fail) throw new InvalidOperationException();
            return Encoding.UTF8.GetBytes("output");
        }
    }

    private sealed class MemoryStreams : IInputFileStreamOpener, IOutputFileStreamOpener
    {
        public MemoryStream Output { get; } = new();
        public List<string> Paths { get; } = [];
        Stream IInputFileStreamOpener.Open(string path)
        {
            Paths.Add(path);
            return new MemoryStream(Encoding.UTF8.GetBytes("input"));
        }
        Stream IOutputFileStreamOpener.Open(string path)
        {
            Paths.Add(path);
            return Output;
        }
    }

    private sealed class BuildEngine : IBuildEngine
    {
        public List<BuildErrorEventArgs> Errors { get; } = [];
        public bool ContinueOnError => false;
        public int LineNumberOfTaskNode => 0;
        public int ColumnNumberOfTaskNode => 0;
        public string ProjectFileOfTaskNode => "";
        public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e);
        public void LogWarningEvent(BuildWarningEventArgs e) { }
        public void LogMessageEvent(BuildMessageEventArgs e) { }
        public void LogCustomEvent(CustomBuildEventArgs e) { }
        public bool BuildProjectFile(string projectFileName, string[] targetNames, IDictionary globalProperties, IDictionary targetOutputs) => throw new NotSupportedException();
    }
}
