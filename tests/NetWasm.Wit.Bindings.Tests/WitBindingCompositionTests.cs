using NetWasm.Wit.Bindings;

namespace NetWasm.Wit.Bindings.Tests;

public sealed class WitBindingCompositionTests
{
    [Fact]
    public void RootsExposeOnlyTheirRequestedCapability()
    {
#pragma warning disable CA1859 // Contract tests deliberately dispatch through one-action interfaces.
        IWitCSharpBindingGenerator generator = WitBindingCompositionRoot.Create();
        IWitBindingCommand command = WitBindingToolCompositionRoot.Create();
#pragma warning restore CA1859

        Assert.NotNull(generator);
        Assert.NotNull(command);
    }
}
