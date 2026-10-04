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

    [Theory]
    [InlineData("wasm32")]
    [InlineData("wasm64")]
    public void CompileLibraryInvokesTheRealCompilerWithoutAMain(string target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("WorkerLibrary", """
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices.JavaScript;
            public static class Worker
            {
                private static int counter;
                [ModuleInitializer]
                public static void Initialize() => counter = 40;
                [JSExport("add")]
                public static int Add(int left, int right) => counter + left + right;
            }
            """);
        using var services = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
        var compiler = Assert.IsAssignableFrom<IManagedModuleCompiler>(
            new ManagedModuleCompiler(new ManagedEntryPointReader(), new WasmTargetResolver(),
                new NetWasmCompilationInvoker(services.GetRequiredService<INetWasmCompiler>())));
        var request = new ManagedModuleCompileRequest(
            assembly, [assets.CoreLib], [], target, null, null, false, "None",
            Path.Combine(assets.Directory, "obj"),
            Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            "netwasm:platform@1.0.0/platform", CompileAsLibrary: true,
            UseJavaScriptExportBoundary: true);

        var result = compiler.Compile(request);
        var repeated = compiler.Compile(request);

        Assert.NotEmpty(result.CoreModule);
        Assert.Null(result.EntryPoint);
        Assert.Equal(target, result.Target);
        Assert.Equal(target, result.InteropManifest.Target);
        Assert.Equal(result.CoreModule, repeated.CoreModule);
        Assert.Contains(result.InteropManifest.Exports, export => export.Name == "add");
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
