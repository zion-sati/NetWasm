using NetWasm.Hosting.Build.Environment;
using NetWasm.Toolchain.Prerequisites;

namespace NetWasm.Hosting.Build.Tests;

public sealed class BuildEnvironmentCacheTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("netwasm-environment-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void StampsTheSameStateAlike()
    {
        var toolchain = Package("toolchain", ("tools/a.txt", "a"), ("tools/nested/b.txt", "bb"));
        var hostTools = Package("host-tools", ("tools/bin/node", "node"));
        var stamper = new BuildEnvironmentStamper(new Variables());
        var request = new HostingBuildEnvironmentRequest(toolchain, hostTools, "id", "1.0", "linux-x64");

        Assert.Equal(stamper.Stamp(request), stamper.Stamp(request with { }));
        Assert.Matches("^[0-9a-f]{64}$", stamper.Stamp(request));
    }

    [Theory]
    [InlineData("size")]
    [InlineData("time")]
    [InlineData("added")]
    [InlineData("variable")]
    [InlineData("request")]
    [InlineData("missing-host-tools")]
    public void StampsWhatResolutionReads(string change)
    {
        var toolchain = Package("toolchain", ("tools/a.txt", "a"));
        var hostTools = Package("host-tools", ("tools/bin/node", "node"));
        var variables = new Variables();
        var stamper = new BuildEnvironmentStamper(variables);
        var request = new HostingBuildEnvironmentRequest(toolchain, hostTools, "id", "1.0", "linux-x64");
        var before = stamper.Stamp(request);
        var file = Path.Combine(toolchain, "tools", "a.txt");

        switch (change)
        {
            case "size": File.WriteAllText(file, "longer"); File.SetLastWriteTimeUtc(file, File.GetLastWriteTimeUtc(file)); break;
            case "time": File.SetLastWriteTimeUtc(file, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)); break;
            case "added": File.WriteAllText(Path.Combine(toolchain, "tools", "c.txt"), "c"); break;
            case "variable": variables.Values["NETWASM_NODE_PATH"] = "/opt/node"; break;
            case "request": request = request with { HostToolsPackageVersion = "2.0" }; break;
            case "missing-host-tools": Directory.Delete(hostTools, recursive: true); break;
        }

        Assert.NotEqual(before, stamper.Stamp(request));
    }

    [Fact]
    public void StampsARequestWithoutHostTools()
    {
        var stamper = new BuildEnvironmentStamper(new Variables());
        var request = new HostingBuildEnvironmentRequest(Package("toolchain", ("a", "a")));

        Assert.NotEqual(stamper.Stamp(request), stamper.Stamp(request with { ToolchainPackageRoot = "" }));
        Assert.Throws<ArgumentNullException>(() => stamper.Stamp(null!));
        Assert.Throws<ArgumentNullException>(() => new BuildEnvironmentStamper(null!));
    }

    [Fact]
    public void ReadsBackWhatItWroteUnderTheSameStamp()
    {
        var tool = Path.Combine(Package("tools", ("node", "node")), "node");
        var path = Path.Combine(_root, "obj", "netwasm", "build-environment.json");
        var outputs = new Dictionary<string, string> { ["NodePath"] = tool, ["NodeVersion"] = "24.0.0" };

        new BuildEnvironmentCacheWriter().Write(path, "stamp", outputs);

        Assert.Equal(outputs, new BuildEnvironmentCacheReader().Read(path, "stamp"));
        Assert.Null(new BuildEnvironmentCacheReader().Read(path, "other"));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));
    }

    [Fact]
    public void MissesWhenANamedFileChanged()
    {
        var tool = Path.Combine(Package("tools", ("node", "node")), "node");
        var path = Path.Combine(_root, "build-environment.json");
        new BuildEnvironmentCacheWriter().Write(path, "stamp", new Dictionary<string, string> { ["NodePath"] = tool });

        File.WriteAllText(tool, "a different node");

        Assert.Null(new BuildEnvironmentCacheReader().Read(path, "stamp"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"Stamp\":\"stamp\",\"Outputs\":{}}")]
    public void MissesWithoutAUsableCache(string? content)
    {
        var path = Path.Combine(_root, "build-environment.json");
        if (content is not null)
        {
            File.WriteAllText(path, content);
        }

        Assert.Null(new BuildEnvironmentCacheReader().Read(path, "stamp"));
    }

    [Fact]
    public void WritingWhereItCannotLeavesNoCache()
    {
        var blocker = Path.Combine(_root, "file");
        File.WriteAllText(blocker, "not a directory");
        var path = Path.Combine(blocker, "build-environment.json");

        new BuildEnvironmentCacheWriter().Write(path, "stamp", new Dictionary<string, string>());

        Assert.Null(new BuildEnvironmentCacheReader().Read(path, "stamp"));
    }

    [Fact]
    public void RejectsMissingArguments()
    {
        var reader = new BuildEnvironmentCacheReader();
        var writer = new BuildEnvironmentCacheWriter();
        Assert.Throws<ArgumentException>(() => reader.Read("", "stamp"));
        Assert.Throws<ArgumentException>(() => reader.Read("path", ""));
        Assert.Throws<ArgumentException>(() => writer.Write("", "stamp", new Dictionary<string, string>()));
        Assert.Throws<ArgumentException>(() => writer.Write("path", "", new Dictionary<string, string>()));
        Assert.Throws<ArgumentNullException>(() => writer.Write("path", "stamp", null!));
        Assert.Null(BuildEnvironmentFileStamp.Of(Path.Combine(_root, "missing")));
    }

    private string Package(string name, params (string Path, string Content)[] files)
    {
        var root = Path.Combine(_root, name);
        foreach (var (path, content) in files)
        {
            var full = Path.Combine(root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }
        return root;
    }

    private sealed class Variables : IHostEnvironmentVariableReader
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

        public string? Read(string name) => Values.GetValueOrDefault(name);
    }
}
