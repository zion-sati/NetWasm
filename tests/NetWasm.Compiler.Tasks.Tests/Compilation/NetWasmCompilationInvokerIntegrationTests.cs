using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Tasks.Compilation;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tasks.Tests.Compilation;

public sealed class NetWasmCompilationInvokerIntegrationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, "wasm32")]
    [InlineData(WasmTarget.Wasm64, "wasm64")]
    public void CompileInvokesTheCompilerAndMapsItsResult(
        WasmTarget target,
        string expectedTarget)
    {
        using var assets = TestAssets.Create();
        using var services = new ServiceCollection()
            .AddNetWasmCompiler()
            .BuildServiceProvider();
        var invoker = Assert.IsAssignableFrom<INetWasmCompilationInvoker>(
            new NetWasmCompilationInvoker(
                services.GetRequiredService<INetWasmCompiler>()));

        var result = invoker.Compile(new CompilerOptions(
            assets.Application,
            [assets.Library, assets.CoreLib],
            "NetWasm.Fixtures.Application.EntryPoint",
            "Run",
            ImmutableArray<RequestedExport>.Empty,
            target,
            WitPath: Path.Combine(
                assets.Root,
                "wit",
                "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform@1.0.0/platform"));

        Assert.NotEmpty(result.CoreModule);
        Assert.True(result.StaticDataEnd > 0);
        Assert.Equal(expectedTarget, result.Target);
        Assert.Equal(expectedTarget, result.InteropManifest.Target);
    }

    [Fact]
    public void ConstructorAndCompileRejectMissingInputs()
    {
        using var services = new ServiceCollection()
            .AddNetWasmCompiler()
            .BuildServiceProvider();
        var invoker = new NetWasmCompilationInvoker(
            services.GetRequiredService<INetWasmCompiler>());

        Assert.Throws<ArgumentNullException>(() =>
            new NetWasmCompilationInvoker(null!));
        Assert.Throws<ArgumentNullException>(() => invoker.Compile(null!));
    }
}
