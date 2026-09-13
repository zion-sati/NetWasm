using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Tasks.ComponentModel;
using NetWasm.Compiler.Tasks.Composition;

namespace NetWasm.Compiler.Tasks.Tests.ComponentModel;

public sealed class CompilerTaskCompositionTests
{
    [Fact]
    public void WitProjectionFactoryCreatesConfiguredSession()
    {
        var factory = CompilerTaskComposition.CreateWitFunctionProjectionSessionFactory();

        using var session = factory.Create(new ExternalToolCommand(
            "node",
            ["run-wasm-tools.mjs", "wasm-tools.wasm"]));

        Assert.IsType<WitFunctionProjectionSession>(session);
    }
}
