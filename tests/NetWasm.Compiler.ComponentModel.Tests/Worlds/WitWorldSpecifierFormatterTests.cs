using NetWasm.Compiler.ComponentModel.Worlds;

namespace NetWasm.Compiler.ComponentModel.Tests.Worlds;

public sealed class WitWorldSpecifierFormatterTests
{
    [Fact]
    public void FormatsVersionedWorldForWasmTools()
    {
        var formatter = AsFormatter(new WitWorldSpecifierFormatter());
        var result = formatter.Format(
            new WitWorld(0, "async-command", "netwasm:component@1.0.0", [], []));

        Assert.Equal("netwasm:component/async-command@1.0.0", result);
    }

    [Fact]
    public void FormatsUnversionedWorldForWasmTools()
    {
        var formatter = AsFormatter(new WitWorldSpecifierFormatter());
        var result = formatter.Format(
            new WitWorld(0, "command", "example:component", [], []));

        Assert.Equal("example:component/command", result);
    }

    // wasm-tools reads a specifier as WIT, so a name that is a keyword keeps its '%'.
    [Theory]
    [InlineData("test:async", "y", "test:%async/y")]
    [InlineData("stream:world@1.0.0", "export", "%stream:%world/%export@1.0.0")]
    [InlineData("error-context:map", "async-x", "%error-context:%map/async-x")]
    public void EscapesKeywordsAndKeepsTheVersion(string package, string world, string expected)
    {
        var formatter = AsFormatter(new WitWorldSpecifierFormatter());

        Assert.Equal(expected, formatter.Format(new WitWorld(0, world, package, [], [])));
    }

    [Fact]
    public void RejectsNullWorld()
    {
        var formatter = AsFormatter(new WitWorldSpecifierFormatter());

        Assert.Throws<ArgumentNullException>(() => formatter.Format(null!));
    }

    private static IWitWorldSpecifierFormatter AsFormatter(object formatter) =>
        (IWitWorldSpecifierFormatter)formatter;
}
