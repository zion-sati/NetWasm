using NetWasm.Runtime.Pack.Materialization;
using NetWasm.Runtime.Pack.Tests.TestSupport;

namespace NetWasm.Runtime.Pack.Tests.Materialization;

public sealed class RuntimeLinkArgumentBuilderTests
{
    [Theory]
    [InlineData("wasm32", "-mwasm32")]
    [InlineData("wasm64", "-mwasm64")]
    public void BuildsTargetSpecificClosedWorldLinkArguments(string target, string machine)
    {
        using var directory = new TemporaryDirectory();
        var manifest = RuntimePackTestData.Manifest();
        var selected = RuntimePackTestData.Target(target);
        var arguments = new RuntimeLinkArgumentBuilder().Build(new(
            manifest,
            selected,
            RuntimePackTestData.Layout(target),
            directory.Path,
            directory.PathTo("output/runtime.wasm")));

        Assert.Equal(machine, arguments[0]);
        Assert.Contains("--whole-archive", arguments);
        Assert.Contains(Path.GetFullPath(directory.PathTo(selected.RuntimeArchive.Path)), arguments);
        Assert.Contains(Path.GetFullPath(directory.PathTo(selected.LinkInputs[0].Path)), arguments);
        Assert.DoesNotContain("--undefined=__emscripten_environ_constructor", arguments);
        Assert.Contains("--no-stack-first", arguments);
        Assert.Contains($"--global-base={RuntimePackTestData.Layout(target).RuntimeGlobalBase}", arguments);
        Assert.Contains("stack-size=65536", arguments);
        Assert.Contains($"--initial-memory={RuntimePackTestData.Layout(target).InitialMemorySizeBytes}", arguments);
        Assert.Contains($"--max-memory={RuntimePackTestData.Layout(target).MaximumMemorySizeBytes}", arguments);
        Assert.Contains("--export=initialize", arguments);
        Assert.Contains("--export=allocate", arguments);
        Assert.Equal(Path.GetFullPath(directory.PathTo("output/runtime.wasm")), arguments[^1]);
        Assert.DoesNotContain("--allow-undefined", arguments);
    }

    [Fact]
    public void RejectsAssetOutsidePackRoot()
    {
        using var directory = new TemporaryDirectory();
        var target = RuntimePackTestData.Target("wasm32") with
        {
            RuntimeArchive = RuntimePackTestData.Asset("../outside.a"),
        };

        Assert.Throws<InvalidOperationException>(() => new RuntimeLinkArgumentBuilder().Build(new(
            RuntimePackTestData.Manifest(),
            target,
            RuntimePackTestData.Layout(),
            directory.Path,
            directory.PathTo("runtime.wasm"))));
    }

    [Fact]
    public void AcceptsAssetRootWithTrailingSeparator()
    {
        using var directory = new TemporaryDirectory();
        var arguments = new RuntimeLinkArgumentBuilder().Build(new(
            RuntimePackTestData.Manifest(),
            RuntimePackTestData.Target("wasm32"),
            RuntimePackTestData.Layout(),
            directory.Path + Path.DirectorySeparatorChar,
            directory.PathTo("runtime.wasm")));

        Assert.Contains(
            Path.GetFullPath(directory.PathTo("wasm32/libnetwasm-runtime.a")),
            arguments);
    }

    [Fact]
    public void RejectsNullRequest() =>
        Assert.Throws<ArgumentNullException>(() => new RuntimeLinkArgumentBuilder().Build(null!));
}
