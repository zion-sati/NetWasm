using NetWasm.Compiler.Core;
using NetWasm.Compiler.Tasks.ComponentModel;

namespace NetWasm.Compiler.Tasks.Tests.ComponentModel;

public sealed class ComponentWitWorldSelectorTests
{
    [Fact]
    public void KeepsDefaultWorldWithoutReachableCapabilityImport()
    {
        var selection = CreateSelector().Select(new(
            "command.wit",
            "command",
            [new("http.wit", "http-command", "wasi:http@0.2.11/")],
            []));

        Assert.Equal("command.wit", selection.Path);
        Assert.Equal("command", selection.World);
    }

    [Fact]
    public void SelectsVariantFromReachableCanonicalInterface()
    {
        var selection = CreateSelector().Select(new(
            "async-command.wit",
            "async-command",
            [new("http.wit", "async-http-command", "wasi:http@0.2.11/")],
            [new HostInteropWitImport(
                "wasi:http@0.2.11/outgoing-handler",
                "handle")]));

        Assert.Equal("http.wit", selection.Path);
        Assert.Equal("async-http-command", selection.World);
    }

    [Fact]
    public void RejectsInvalidOrAmbiguousVariantCatalog()
    {
        var selector = CreateSelector();
        Assert.Throws<ArgumentNullException>(() => selector.Select(null!));
        Assert.Throws<ArgumentException>(() => selector.Select(new(
            "",
            null,
            [],
            [])));
        Assert.Throws<ArgumentException>(() => selector.Select(new(
            "command.wit",
            null,
            [new("", null, "")],
            [])));
        Assert.Throws<InvalidOperationException>(() => selector.Select(new(
            "command.wit",
            null,
            [
                new("first.wit", null, "wasi:http@"),
                new("second.wit", null, "wasi:http@0.2.11/"),
            ],
            [new HostInteropWitImport(
                "wasi:http@0.2.11/types",
                "[resource-drop]fields")])));
    }

    private static ComponentWitWorldSelector CreateSelector() =>
        new ComponentWitWorldSelector();
}
