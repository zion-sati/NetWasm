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

    [Fact]
    public void RejectsMissingCollaborators()
    {
        var tools = new RecordingTools(new ToolResult(0, "", ""));
        Assert.Throws<ArgumentNullException>(() => new CachingWasmTools(null!, _command, _root));
        Assert.Throws<ArgumentNullException>(() => new CachingWasmTools(tools, null!, _root));
        Assert.Throws<ArgumentException>(() => new CachingWasmTools(tools, _command, " "));
        Assert.Throws<ArgumentNullException>(() => Create(tools).Run(null!));
        Assert.EndsWith(Path.Combine("NetWasm", "wasm-tools-cache"), CachingWasmTools.DefaultDirectory());
    }

    private CachingWasmTools Create(IWasmTools tools) => new(tools, _command, Path.Combine(_root, "cache"));

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
