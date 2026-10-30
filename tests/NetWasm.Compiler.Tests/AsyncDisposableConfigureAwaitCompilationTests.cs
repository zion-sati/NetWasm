using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class AsyncDisposableConfigureAwaitCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void ConfiguredAwaitUsingBindsToTheStandardExtension(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = optimize
            ? assets.CompileOptimizedSource("AsyncDisposableConfigureAwaitFixture", Source)
            : assets.CompileSource("AsyncDisposableConfigureAwaitFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "AsyncDisposableConfigureAwaitFixture.EntryPoint",
            "Run", [new("run", "AsyncDisposableConfigureAwaitFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/async-platform@1.0.0"));

        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    // Both overloads of ConfigureAwait on IAsyncDisposable used to be declared, so these
    // await using statements failed to compile with CS0121.
    private const string Source = """
        using System;
        using System.Threading.Tasks;

        namespace AsyncDisposableConfigureAwaitFixture;

        public sealed class Resource : IAsyncDisposable
        {
            public int Disposals;

            public ValueTask DisposeAsync()
            {
                Disposals++;
                return ValueTask.CompletedTask;
            }
        }

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var resource = new Resource();
                var statement = UseStatementAsync(resource);
                if (!statement.IsCompletedSuccessfully || statement.Result != 7 || resource.Disposals != 1) return 1;
                var declaration = UseDeclarationAsync(resource);
                if (!declaration.IsCompletedSuccessfully || declaration.Result != 8 || resource.Disposals != 2) return 2;
                resource.ConfigureAwait(true).DisposeAsync().GetAwaiter().GetResult();
                if (resource.Disposals != 3) return 3;
                return input + 1;
            }

            private static async Task<int> UseStatementAsync(Resource resource)
            {
                await using (resource.ConfigureAwait(false))
                {
                    return 7;
                }
            }

            private static async Task<int> UseDeclarationAsync(Resource resource)
            {
                await using var configured = resource.ConfigureAwait(false);
                return 8;
            }
        }
        """;
}
