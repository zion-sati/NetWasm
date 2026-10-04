using NetWasm.Compiler.Tasks.ComponentModel;

namespace NetWasm.Compiler.Tasks.Tests.ComponentModel;

public sealed class WitWorkerPackageMaterializerTests
{
    [Fact]
    public void MirrorsMembershipAddsPlatformAndLeavesUnchangedOutputAlone()
    {
        using var files = new TemporaryTree();
        var authored = files.Directory("authored");
        var output = files.Path("obj/resolved");
        var platform = files.File("toolchain/compiler.wit.wasm", "platform");
        files.File("authored/worker.wit", "package example:worker@1.0.0;");
        var removable = files.File("authored/deps/example.wit", "dependency");
        var actor = new WitWorkerPackageMaterializer();

        Assert.Equal(output, actor.Materialize(new(authored, platform, output)));
        Assert.Equal(
            "platform",
            File.ReadAllText(Path.Combine(output, "deps/platform.wit.wasm")));
        var worker = Path.Combine(output, "worker.wit");
        var timestamp = File.GetLastWriteTimeUtc(worker);

        Assert.Equal(output, actor.Materialize(new(authored, platform, output)));
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(worker));

        File.Delete(removable);
        actor.Materialize(new(authored, platform, output));
        Assert.False(File.Exists(Path.Combine(output, "deps/example.wit")));
    }

    [Fact]
    public void AcceptsMatchingAuthoredPlatformAndRejectsConflicts()
    {
        using var files = new TemporaryTree();
        var authored = files.Directory("authored/deps");
        var sourceRoot = Directory.GetParent(authored)!.FullName;
        var platform = files.File("toolchain/compiler.wit.wasm", "platform");
        var authoredPlatform = files.File(
            "authored/deps/platform.wit.wasm",
            "platform");
        var actor = new WitWorkerPackageMaterializer();

        var output = files.Path("obj/resolved");
        actor.Materialize(new(sourceRoot, platform, output));
        Assert.Equal("platform", File.ReadAllText(Path.Combine(
            output,
            "deps/platform.wit.wasm")));

        File.WriteAllText(authoredPlatform, "conflict");
        Assert.Throws<InvalidDataException>(() =>
            actor.Materialize(new(sourceRoot, platform, output)));
    }

    [Fact]
    public void PreservesClosedBinaryAndRejectsInvalidBoundaries()
    {
        using var files = new TemporaryTree();
        var binary = files.File("worker.wit.wasm", "closed");
        var actor = new WitWorkerPackageMaterializer();

        Assert.Equal(
            binary,
            actor.Materialize(new(binary, "missing-platform", files.Path("obj"))));
        Assert.Throws<DirectoryNotFoundException>(() => actor.Materialize(new(
            files.Path("missing"),
            files.Path("platform"),
            files.Path("output"))));

        var authored = files.Directory("authored");
        Assert.Throws<FileNotFoundException>(() => actor.Materialize(new(
            authored,
            files.Path("missing-platform"),
            files.Path("output"))));
        var platform = files.File("platform.wit.wasm", "platform");
        Assert.Throws<ArgumentException>(() => actor.Materialize(new(
            authored,
            platform,
            Path.Combine(authored, "obj"))));
    }

    private sealed class TemporaryTree : IDisposable
    {
        private readonly string _root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"netwasm-wit-worker-{Guid.NewGuid():N}");

        public string Path(string relative) =>
            System.IO.Path.GetFullPath(System.IO.Path.Combine(_root, relative));

        public string Directory(string relative)
        {
            var path = Path(relative);
            System.IO.Directory.CreateDirectory(path);
            return path;
        }

        public string File(string relative, string contents)
        {
            var path = Path(relative);
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllText(path, contents);
            return path;
        }

        public void Dispose()
        {
            if (System.IO.Directory.Exists(_root))
            {
                System.IO.Directory.Delete(_root, recursive: true);
            }
        }
    }
}
