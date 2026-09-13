using NetWasm.Compiler.Core;
using NetWasm.Compiler.Tasks.Compilation;

namespace NetWasm.Compiler.Tasks.Tests.Compilation;

public sealed class WasmTargetResolverTests
{
    [Theory]
    [InlineData("wasm32", WasmTarget.Wasm32)]
    [InlineData("wasm64", WasmTarget.Wasm64)]
    public void ResolveSelectsTheRequestedTarget(string value, WasmTarget expected)
    {
        var resolver = Assert.IsAssignableFrom<IWasmTargetResolver>(new WasmTargetResolver());

        Assert.Equal(expected, resolver.Resolve(value));
    }

    [Fact]
    public void ResolveRejectsAnUnknownTarget()
    {
        var resolver = Assert.IsAssignableFrom<IWasmTargetResolver>(new WasmTargetResolver());

        Assert.Throws<ArgumentException>(() => resolver.Resolve("wasm128"));
    }
}
