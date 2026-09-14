using NetWasm.Wit.Bindings;
using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Wit.Bindings.Tests;

public sealed class WitBindingCompositionTests
{
    [Fact]
    public void RootsExposeOnlyTheirRequestedCapability()
    {
#pragma warning disable CA1859 // Contract tests deliberately dispatch through one-action interfaces.
        IWitCSharpBindingGenerator generator = WitBindingCompositionRoot.Create();
        IWitBindingCommand command = WitBindingToolCompositionRoot.Create(
            new ExternalToolCommand("node", ["runner.mjs", "wasm-tools.wasm"]));
#pragma warning restore CA1859

        Assert.NotNull(generator);
        Assert.NotNull(command);
    }
}
