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

    [Fact]
    public void RejectsNullWorld()
    {
        var formatter = AsFormatter(new WitWorldSpecifierFormatter());

        Assert.Throws<ArgumentNullException>(() => formatter.Format(null!));
    }

    private static IWitWorldSpecifierFormatter AsFormatter(object formatter) =>
        (IWitWorldSpecifierFormatter)formatter;
}
