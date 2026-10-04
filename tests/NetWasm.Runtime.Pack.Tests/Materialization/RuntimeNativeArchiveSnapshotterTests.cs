using System.Collections.Immutable;
using NetWasm.Runtime.Pack.Materialization;
using NetWasm.Runtime.Pack.Tests.TestSupport;

namespace NetWasm.Runtime.Pack.Tests.Materialization;

public sealed class RuntimeNativeArchiveSnapshotterTests
{
    [Fact]
    public void ReturnsVerifiedOwnedCopiesWithoutDependingOnSubsequentOriginalChanges()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.Write("libmule.a", "!<arch>\noriginal bytes");
        var owned = Directory.CreateDirectory(Path.Combine(directory.Path, "owned")).FullName;
        var provider = new RuntimeNativeLibrary("mule", "wasm32", source, RuntimePackTestData.Digest);
        var reader = new RecordingReader { OnRead = path =>
        {
            Assert.Equal("!<arch>\noriginal bytes", File.ReadAllText(path));
            File.WriteAllText(source, "changed original");
        } };
        var snapshotter = Assert.IsAssignableFrom<IRuntimeNativeArchiveSnapshotter>(new RuntimeNativeArchiveSnapshotter(reader));

        var result = Assert.Single(snapshotter.Snapshot(new([provider], owned)));

        Assert.Equal(provider, result.Provider);
        Assert.Equal(Path.Combine(owned, "native-0.a"), result.SnapshotPath);
        Assert.Equal([result.SnapshotPath], reader.Paths);
        Assert.Equal("!<arch>\noriginal bytes", File.ReadAllText(result.SnapshotPath));
        Assert.Equal("changed original", File.ReadAllText(source));
    }

    [Fact]
    public void MismatchedSnapshotBytesFailBeforeReturningProviderProof()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.Write("libmule.a", "changed bytes");
        var reader = new RecordingReader { Digest = new string('b', 64) };
        var provider = new RuntimeNativeLibrary("mule", "wasm64", source, RuntimePackTestData.Digest);

        var error = Assert.Throws<InvalidOperationException>(() =>
            new RuntimeNativeArchiveSnapshotter(reader).Snapshot(new([provider], directory.Path)));

        Assert.Contains("changed", error.Message, StringComparison.Ordinal);
        Assert.Single(reader.Paths);
    }

    [Fact]
    public void NeverOverwritesAnExistingWorkspaceFile()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.Write("libmule.a", "source");
        var existing = directory.Write("native-0.a", "existing");
        var reader = new RecordingReader();

        Assert.Throws<IOException>(() => new RuntimeNativeArchiveSnapshotter(reader).Snapshot(
            new([new("mule", "wasm32", source, RuntimePackTestData.Digest)], directory.Path)));

        Assert.Equal("existing", File.ReadAllText(existing));
        Assert.Empty(reader.Paths);
    }

    [Fact]
    public void InvalidContractsRejectBeforeAnyCopyOrRead()
    {
        var reader = new RecordingReader();
        var snapshotter = new RuntimeNativeArchiveSnapshotter(reader);
        var provider = new RuntimeNativeLibrary("mule", "wasm32", "/missing/libmule.a", RuntimePackTestData.Digest);
        Assert.Throws<ArgumentNullException>(() => new RuntimeNativeArchiveSnapshotter(null!));
        Assert.Throws<ArgumentNullException>(() => snapshotter.Snapshot(null!));
        Assert.Throws<ArgumentException>(() => snapshotter.Snapshot(new([], " ")));
        foreach (var request in new RuntimeNativeArchiveSnapshotRequest[]
        {
            new([], "relative"), new(default, "/owned"), new([null!], "/owned"),
            new([provider with { Path = "relative.a" }], "/owned"),
            new([provider with { Sha256 = "short" }], "/owned"),
            new([provider with { Sha256 = new string('g', 64) }], "/owned"),
            new([provider, provider], "/owned"),
        })
            Assert.Throws<InvalidOperationException>(() => snapshotter.Snapshot(request));
        Assert.Empty(snapshotter.Snapshot(new([], "/owned")));
        Assert.Empty(reader.Paths);
    }

    private sealed class RecordingReader : IRuntimeNativeArchiveReader
    {
        public string Digest { get; init; } = RuntimePackTestData.Digest;
        public Action<string>? OnRead { get; init; }
        public List<string> Paths { get; } = [];
        public string Read(string path) { Paths.Add(path); OnRead?.Invoke(path); return Digest; }
    }
}
