// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Globalization;
using System.Text;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// A runtime-async method with N awaits, each inside a try/catch inside a try/finally, as large async methods have them:
// its lowering dispatches to every await from the method's entry and leaves every protected block through one
// suspension exit. Leaves continued to the method's end, dispatchers that tested every exit against every block and
// passes that compared each region with every other made such a module grow with N squared (and the compiler run out
// of memory at 400 awaits). The module now grows with N. Compiled without runtime-async=on, the same method is Roslyn's
// state machine, whose MoveNext the compiler rewrites into the runtime async method (a dispatch of N cases and N
// protected blocks' own dispatches folded away): it grows with N too, and its machine is not kept.
public sealed class RuntimeAsyncProtectedAwaitScalingCompilationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheModuleGrowsWithTheAwaitsNotTheirSquare(bool stateMachines)
    {
        using var assets = TestAssets.Create();
        var small = Size(assets, 100, stateMachines);
        var medium = Size(assets, 200, stateMachines);
        var large = Size(assets, 400, stateMachines);
        // Doubling the awaits doubles a linear increment but quadruples a quadratic one.
        Assert.True(
            large - medium <= (medium - small) * 5 / 2,
            $"100 awaits: {small} bytes; 200: {medium}; 400: {large}");
    }

    // An async iterator with N yields, each after an await inside a try/finally: its MoveNextAsync is its machine's
    // MoveNext rewritten, where each protected block tests the state, so the states a test can see grow with N as the
    // tests do. The module grows with N, and no MoveNext is kept.
    [Fact]
    public void AnAsyncIteratorGrowsWithItsYieldsNotTheirSquare()
    {
        using var assets = TestAssets.Create();
        var small = IteratorSize(assets, 100);
        var medium = IteratorSize(assets, 200);
        var large = IteratorSize(assets, 400);
        Assert.True(
            large - medium <= (medium - small) * 5 / 2,
            $"100 yields: {small} bytes; 200: {medium}; 400: {large}");
    }

    private static long IteratorSize(TestAssets assets, int yields)
    {
        var assembly = assets.CompileSourceWithCompiler($"ProtectedYields{yields}", IteratorSource(yields),
            TestAssets.RepositorySdkVersion, "latest", optimize: true, features: []);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Optimization: FinalWasmOptimization.None));
        Assert.DoesNotContain(result.Program.Methods.Values, body =>
            body.Method.CanonicalName.Contains("<Many>d__", StringComparison.Ordinal) &&
            body.Method.CanonicalName.Contains(".MoveNext()", StringComparison.Ordinal));
        return result.ApplicationModule.Length;
    }

    private static string IteratorSource(int yields)
    {
        var source = new StringBuilder("""
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;

            public static class EntryPoint
            {
                private static int s_finished;

                private static async IAsyncEnumerable<int> Many()
                {

            """);
        for (var index = 0; index < yields; index++)
        {
            source.AppendLine(CultureInfo.InvariantCulture,
                $"        try {{ await Task.Yield(); yield return {index}; }} finally {{ s_finished++; }}");
        }
        source.AppendLine("""
                }

                public static int Run(int value)
                {
                    var enumerator = Many().GetAsyncEnumerator();
                    var total = 0;
                    while (enumerator.MoveNextAsync().AsTask().Result) total += enumerator.Current;
                    return total + s_finished + value;
                }
            }
            """);
        return source.ToString();
    }

    private static long Size(TestAssets assets, int awaits, bool stateMachines)
    {
        var assembly = assets.CompileSourceWithCompiler(
            $"ProtectedAwaits{awaits}{(stateMachines ? "Machine" : "")}",
            Source(awaits),
            TestAssets.RepositorySdkVersion,
            "latest",
            optimize: true,
            features: stateMachines ? [] : ["runtime-async=on"]);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Optimization: FinalWasmOptimization.None));
        Assert.DoesNotContain(result.Program.Methods.Values, body =>
            body.Method.CanonicalName.Contains("<Many>d__", StringComparison.Ordinal));
        return result.ApplicationModule.Length;
    }

    private static string Source(int awaits)
    {
        var source = new StringBuilder("""
            using System;
            using System.Threading.Tasks;

            public static class EntryPoint
            {
                private static int s_finished;

                private static async Task<int> Step(int value)
                {
                    await Task.Yield();
                    return value;
                }

                private static async Task<int> Many(int value)
                {
                    var total = value;

            """);
        for (var index = 0; index < awaits; index++)
        {
            source.AppendLine(CultureInfo.InvariantCulture,
                $"        try {{ try {{ total += await Step({index}); }} catch (InvalidOperationException) {{ total--; }} }} finally {{ s_finished++; }}");
        }
        source.AppendLine("""
                    return total + s_finished;
                }

                public static int Run(int value) => Many(value).Result;
            }
            """);
        return source.ToString();
    }
}
