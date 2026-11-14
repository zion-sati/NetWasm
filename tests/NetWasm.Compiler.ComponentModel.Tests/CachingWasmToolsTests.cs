using System.Collections.Immutable;

namespace NetWasm.Compiler.ComponentModel.Tests;

public sealed class CachingWasmToolsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("netwasm-wasm-tools-cache-").FullName;
    private readonly ExternalToolCommand _command = new("node", ["run-wasm-tools.mjs", "wasm-tools.wasm"]);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void ReusesAPureCommandsResultForTheSameInputs()
    {
        var wit = Input("package.wit.wasm", "wit");
        var tools = new RecordingTools(new ToolResult(0, "{\"json\":1}", "warning"));
        var cache = Create(tools);

        var first = cache.Run("component", "wit", wit, "--json", "--no-docs");
        var second = Create(tools).Run("component", "wit", wit, "--json", "--no-docs");

        Assert.Equal(1, tools.Calls);
        Assert.Equal(first, second);
    }

    [Fact]
    public void RunsAgainWhenAnInputsContentChanges()
    {
        var wit = Input("package.wit.wasm", "wit");
        var tools = new RecordingTools(new ToolResult(0, "out", ""));
        var cache = Create(tools);
        cache.Run("component", "embed", wit, "--world", "w", "--dummy", "-t");

        File.WriteAllText(wit, "changed");
        cache.Run("component", "embed", wit, "--world", "w", "--dummy", "-t");
        cache.Run("component", "embed", wit, "--world", "other", "--dummy", "-t");

        Assert.Equal(3, tools.Calls);
    }

    [Fact]
    public void RestoresTheOutputFileOfACachedParse()
    {
        var source = Input("environment.wat", "(module)");
        var output = Path.Combine(_root, "environment.wasm");
        var tools = new RecordingTools(new ToolResult(0, "", ""), writes: [1, 2, 3]);
        var cache = Create(tools);
        cache.Run("parse", source, "--output", output);
        File.Delete(output);

        var result = cache.Run("parse", source, "-o", output);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, tools.Calls);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(output));
    }

    [Theory]
    [InlineData("validate")]
    [InlineData("directory")]
    [InlineData("failure")]
    [InlineData("bare")]
    public void RunsWhatItDoesNotCache(string shape)
    {
        var tools = new RecordingTools(new ToolResult(shape == "failure" ? 1 : 0, "", "error"));
        var cache = Create(tools);
        string[] arguments = shape switch
        {
            "validate" => ["validate", Input("module.wasm", "m")],
            "directory" => ["component", "wit", _root, "--json"],
            "failure" => ["component", "wit", Input("bad.wit.wasm", "bad"), "--json"],
            "bare" => ["component", "wit"],
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        cache.Run(arguments);
        cache.Run(arguments);

        Assert.Equal(2, tools.Calls);
    }

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 1, 2, 3, 4 })]
    [InlineData(new byte[] { 0x4E, 0x54, 0x57, 0x43, 0, 0, 0, 0 })]
    public void RunsAgainWhenTheCachedEntryIsUnreadable(byte[] entry)
    {
        var wit = Input("package.wit.wasm", "wit");
        var tools = new RecordingTools(new ToolResult(0, "out", ""));
        Create(tools).Run("component", "wit", wit, "--json");
        foreach (var file in Directory.GetFiles(Path.Combine(_root, "cache"), "*", SearchOption.AllDirectories))
        {
            File.WriteAllBytes(file, entry);
        }

        Assert.Equal("out", Create(tools).Run("component", "wit", wit, "--json").StandardOutput);

        Assert.Equal(2, tools.Calls);
    }

    [Fact]
    public void RunsWhenTheCacheCannotBeWritten()
    {
        var blocker = Input("blocker", "not a directory");
        var tools = new RecordingTools(new ToolResult(0, "out", ""));
        var cache = new CachingWasmTools(tools, _command, blocker);
        var wit = Input("package.wit.wasm", "wit");

        cache.Run("component", "wit", wit, "--json");
        cache.Run("component", "wit", wit, "--json");

        Assert.Equal(2, tools.Calls);
    }

    // The tool's own files key its results by size and write time.
    [Fact]
    public void RunsAgainWhenTheToolsOwnFileChanges()
    {
        var script = Input("run-wasm-tools.mjs", "one");
        var tools = new RecordingTools(new ToolResult(0, "out", ""));
        var command = new ExternalToolCommand("node", [script]);
        var wit = Input("package.wit.wasm", "wit");
        new CachingWasmTools(tools, command, Path.Combine(_root, "cache")).Run("component", "wit", wit, "--json");

        File.WriteAllText(script, "two, longer");
        new CachingWasmTools(tools, command, Path.Combine(_root, "cache")).Run("component", "wit", wit, "--json");

        Assert.Equal(2, tools.Calls);
    }

    // An entry cut short in its output, or an output that cannot be put back, runs the tool.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RunsAgainWhenACachedOutputCannotBeRestored(bool truncated)
    {
        var source = Input("environment.wat", "(module)");
        var output = Path.Combine(_root, "environment.wasm");
        var tools = new RecordingTools(new ToolResult(0, "", ""), writes: [1, 2, 3]);
        Create(tools).Run("parse", source, "-o", output);
        if (truncated)
        {
            foreach (var file in Directory.GetFiles(Path.Combine(_root, "cache"), "*", SearchOption.AllDirectories))
                File.WriteAllBytes(file, File.ReadAllBytes(file)[..^2]);
        }
        else
        {
            File.Delete(output);
            Directory.CreateDirectory(output);
        }

        var again = new RecordingTools(new ToolResult(0, "", ""));
        Create(again).Run("parse", source, "-o", truncated ? output : Path.Combine(output, "missing", "out.wasm"));

        Assert.Equal(1, again.Calls);
    }

    // Past the capacity, the least recently used entries go until three quarters remain; a hit counts as a use.
    [Fact]
    public void EvictsTheLeastRecentlyUsedEntriesPastTheCapacity()
    {
        var wit = Input("package.wit.wasm", "wit");
        var tools = new RecordingTools(new ToolResult(0, "out", ""));
        var first = RunAndFindEntry(tools, wit, "a");
        var second = RunAndFindEntry(tools, wit, "b");
        var entry = new FileInfo(first).Length;
        File.SetLastWriteTimeUtc(first, DateTime.UtcNow.AddHours(-2));
        File.SetLastWriteTimeUtc(second, DateTime.UtcNow.AddHours(-1));
        Create(tools).Run("component", "embed", wit, "--world", "a", "--dummy", "-t");

        new CachingWasmTools(tools, _command, Path.Combine(_root, "cache"), capacity: 3 * entry - 1)
            .Run("component", "embed", wit, "--world", "c", "--dummy", "-t");

        Assert.Equal(3, tools.Calls);
        Assert.True(File.Exists(first));
        Assert.False(File.Exists(second));
        Assert.Equal(2, Entries().Length);
    }

    [Fact]
    public void KeepsEntriesWithinTheCapacity()
    {
        var wit = Input("package.wit.wasm", "wit");
        var tools = new RecordingTools(new ToolResult(0, "out", ""));
        var first = RunAndFindEntry(tools, wit, "a");

        new CachingWasmTools(tools, _command, Path.Combine(_root, "cache"), capacity: 2 * new FileInfo(first).Length)
            .Run("component", "embed", wit, "--world", "b", "--dummy", "-t");

        Assert.Equal(2, Entries().Length);
    }

    // An entry this build may not delete stays, and trimming goes on past it.
    [Fact]
    public void TrimSkipsAnEntryItMayNotDelete()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        var wit = Input("package.wit.wasm", "wit");
        var tools = new RecordingTools(new ToolResult(0, "out", ""));
        var kept = RunAndFindEntry(tools, wit, "a");
        File.SetLastWriteTimeUtc(kept, DateTime.UtcNow.AddHours(-1));
        var directory = Path.GetDirectoryName(kept)!;
        File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            // An entry for a world whose key lands in another directory is written, and the write trims.
            var world = Enumerable.Range(0, 64).Select(index => $"w{index}")
                .First(candidate => DirectoryOf(tools, wit, candidate) != Path.GetFileName(directory));
            new CachingWasmTools(tools, _command, Path.Combine(_root, "cache"), capacity: 1)
                .Run("component", "embed", wit, "--world", world, "--dummy", "-t");
            Assert.True(File.Exists(kept));
        }
        finally
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public void ThereIsNoDefaultDirectoryWithoutLocalApplicationData()
    {
        Assert.Null(CachingWasmTools.DirectoryUnder(null));
        Assert.Null(CachingWasmTools.DirectoryUnder(" "));
        Assert.Equal(Path.Combine("data", "NetWasm", "wasm-tools-cache"), CachingWasmTools.DirectoryUnder("data"));
    }

    [Fact]
    public void RejectsMissingCollaborators()
    {
        var tools = new RecordingTools(new ToolResult(0, "", ""));
        Assert.Throws<ArgumentNullException>(() => new CachingWasmTools(null!, _command, _root));
        Assert.Throws<ArgumentNullException>(() => new CachingWasmTools(tools, null!, _root));
        Assert.Throws<ArgumentException>(() => new CachingWasmTools(tools, _command, " "));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CachingWasmTools(tools, _command, _root, capacity: 0));
        Assert.Throws<ArgumentNullException>(() => Create(tools).Run(null!));
        Assert.EndsWith(Path.Combine("NetWasm", "wasm-tools-cache"), CachingWasmTools.DefaultDirectory());
    }

    private CachingWasmTools Create(IWasmTools tools) => new(tools, _command, Path.Combine(_root, "cache"));

    private string[] Entries() => Directory.GetFiles(Path.Combine(_root, "cache"), "*", SearchOption.AllDirectories);

    // The directory a cached embed for the world lands in, found in a separate cache.
    private string DirectoryOf(RecordingTools tools, string wit, string world)
    {
        var probe = Path.Combine(_root, "probe-" + world);
        new CachingWasmTools(tools, _command, probe).Run("component", "embed", wit, "--world", world, "--dummy", "-t");
        return Path.GetFileName(Path.GetDirectoryName(
            Assert.Single(Directory.GetFiles(probe, "*", SearchOption.AllDirectories)))!);
    }

    // Runs a cached embed for the world and returns the entry it added.
    private string RunAndFindEntry(RecordingTools tools, string wit, string world)
    {
        var before = Directory.Exists(Path.Combine(_root, "cache")) ? Entries() : [];
        Create(tools).Run("component", "embed", wit, "--world", world, "--dummy", "-t");
        return Assert.Single(Entries().Except(before));
    }

    private string Input(string name, string content)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }

    private sealed class RecordingTools(ToolResult result, byte[]? writes = null) : IWasmTools
    {
        public int Calls { get; private set; }

        public ToolResult Run(params IEnumerable<string> arguments)
        {
            Calls++;
            var values = arguments.ToImmutableArray();
            var output = values.IndexOf("--output") is >= 0 and var index ? values[index + 1]
                : values.IndexOf("-o") is >= 0 and var shortIndex ? values[shortIndex + 1] : null;
            if (output is not null && writes is not null)
            {
                File.WriteAllBytes(output, writes);
            }
            return result;
        }
    }
}
