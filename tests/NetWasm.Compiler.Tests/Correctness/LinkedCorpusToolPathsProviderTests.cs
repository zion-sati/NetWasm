namespace NetWasm.Compiler.Tests.Correctness;

public sealed class LinkedCorpusToolPathsProviderTests
{
    [Fact]
    public void GetNormalizesExplicitToolsAndDerivesBinaryenPaths()
    {
        var root = Path.GetFullPath("linked-tools");
        var environment = new LinkedCorpusToolEnvironment(
            Path.Combine(root, "emsdk", "."),
            Path.Combine(root, "node"),
            Path.Combine(root, "wasm-tools"));
        var provider = Assert.IsAssignableFrom<ILinkedCorpusToolPathsProvider>(
            new LinkedCorpusToolPathsProvider(environment));

        var tools = provider.Get();

        Assert.Equal(Path.Combine(root, "emsdk"), tools.EmsdkRoot);
        Assert.Equal(Path.Combine(root, "emsdk", "upstream", "bin", "wasm-merge"), tools.Merge);
        Assert.Equal(Path.Combine(root, "emsdk", "upstream", "bin", "wasm-opt"), tools.Optimize);
        Assert.Equal(environment.WasmToolsPath, tools.Validate);
        Assert.Equal(environment.NodePath, tools.Node);
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(0, "relative")]
    [InlineData(1, null)]
    [InlineData(1, "relative")]
    [InlineData(2, null)]
    [InlineData(2, "relative")]
    public void GetRejectsMissingOrRelativeToolRoots(int field, string? value)
    {
        var root = Path.GetFullPath("linked-tools");
        var environment = new LinkedCorpusToolEnvironment(
            field == 0 ? value : Path.Combine(root, "emsdk"),
            field == 1 ? value : Path.Combine(root, "node"),
            field == 2 ? value : Path.Combine(root, "wasm-tools"));
        Assert.Throws<InvalidOperationException>(() =>
            new LinkedCorpusToolPathsProvider(environment).Get());
    }

    [Fact]
    public void GetRejectsNullEnvironment() =>
        Assert.Throws<ArgumentNullException>(() =>
            new LinkedCorpusToolPathsProvider(null!).Get());
}
