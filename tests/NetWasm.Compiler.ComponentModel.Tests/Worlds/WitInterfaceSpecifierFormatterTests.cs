using NetWasm.Compiler.ComponentModel.Worlds;

namespace NetWasm.Compiler.ComponentModel.Tests.Worlds;

public sealed class WitInterfaceSpecifierFormatterTests
{
    [Fact]
    public void FormatsVersionedInterfaceWithTheExactPackageVersion()
    {
        var result = Create().Format(new(0, "environment", "wasi:cli@0.2.11", [], []));

        Assert.Equal("wasi:cli/environment@0.2.11", result);
    }

    [Fact]
    public void FormatsUnversionedInterface()
    {
        var result = Create().Format(new(0, "host", "example:platform", [], []));

        Assert.Equal("example:platform/host", result);
    }

    [Fact]
    public void RejectsNullInterface()
    {
        Assert.Throws<ArgumentNullException>(() => Create().Format(null!));
    }

    private static IWitInterfaceSpecifierFormatter Create() =>
        Assert.IsAssignableFrom<IWitInterfaceSpecifierFormatter>(
            new WitInterfaceSpecifierFormatter());
}
