using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Tests.Worlds;

public sealed class WitWorldSelectionTests
{
    // A package or world named with a WIT keyword is written with '%' in WIT;
    // either spelling selects it (#198).
    [Theory]
    [InlineData("app")]
    [InlineData("example:stream@1.0.0/app")]
    [InlineData("example:stream/app@1.0.0")]
    [InlineData("example:%stream/app@1.0.0")]
    public void SelectsAWorldByAnySpecifierSpelling(string specifier)
    {
        var world = new WitWorld(0, "app", "example:stream@1.0.0", [], []);
        var document = new WitDocument([], [], [world], [], "{}");

        Assert.Same(world, document.SelectWorld(specifier));
    }

    [Fact]
    public void EscapesAKeywordWorldNameToo()
    {
        var world = new WitWorld(0, "export", "example:app@1.0.0", [], []);
        var document = new WitDocument([], [], [world], [], "{}");

        Assert.Same(world, document.SelectWorld("example:app/%export@1.0.0"));
        Assert.Throws<CompilerException>(() => document.SelectWorld("example:%app/export@1.0.0"));
    }
}
