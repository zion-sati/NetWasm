using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static NetWasm.Compiler.Tests.CompilerTestSupport;

public sealed class StaticRootRegistrationCompilationTests
{
    // The JavaScript shim host counts registrations but implements only wasm32 runtime
    // imports; the scenario test below covers both widths on the native runtime.
    [Fact]
    public void RepeatedExportCallsRegisterStaticRootsOnlyOnTheFirstEntry()
    {
        const WasmTarget target = WasmTarget.Wasm32;
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "StaticRootRegistrationFixture",
            """
            namespace StaticRootRegistrationFixture;

            public static class Cache
            {
                public static object First = new();
                public static string Second = "abc";
                public static int[] Third = [1, 2, 3];
            }

            public static class EntryPoint
            {
                public static int Run(int input) => Touch(input);

                public static int Touch(int input)
                {
                    var count = Cache.First is null ? 0 : 1;
                    count += Cache.Second.Length == 3 ? 1 : 0;
                    return input + count + Cache.Third.Length;
                }
            }
            """);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "StaticRootRegistrationFixture.EntryPoint",
            "Run",
            [new RequestedExport("touch", "StaticRootRegistrationFixture.EntryPoint", "Touch")],
            target));

        Assert.Equal(42, ExecuteWithNode(
            result.ApplicationModule,
            assets.Directory,
            37,
            invocationCount: 3,
            exportName: "touch",
            target: target,
            assertStaticRootsRegisterOnce: true));
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void RepeatedExportCallsKeepStaticRootsAliveAcrossCollections(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        var observed = executor.Execute(new CompilationScenario(
            $"StaticRootCollection{(optimize ? "Release" : "Debug")}{target}",
            """
            using System;

            namespace StaticRootCollectionFixture;

            public static class Cache
            {
                public static object First = new();
                public static string Second = string.Concat("ab", "c");
                public static int[] Third = [1, 2, 3];
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    for (var index = 0; index < 512; index++)
                    {
                        _ = new byte[64];
                    }

                    GC.Collect();
                    var count = Cache.First is null ? 0 : 1;
                    count += Cache.Second == "abc" ? 1 : 0;
                    return input + count + Cache.Third[0] + Cache.Third[1] + Cache.Third[2];
                }
            }
            """,
            "StaticRootCollectionFixture.EntryPoint",
            optimize,
            target,
            34,
            [])
        {
            Exports = [new RequestedExport("run", "StaticRootCollectionFixture.EntryPoint", "Run")],
            WitPath = Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld = "netwasm:platform/platform@1.0.0",
            EntryInvocationCount = 3,
        });

        Assert.Equal(42, observed);
    }
}
