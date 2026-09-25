using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class IteratorCompilationTests
{
    [Fact]
    public void CompilerLowersSynchronousYieldIteratorAndForeachDisposal()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "IteratorFixture",
            """
            using System.Collections.Generic;

            namespace IteratorFixture;

            public static class EntryPoint
            {
                private static int _finallyCount;

                public static int Run(int input)
                {
                    _finallyCount = 0;
                    var total = 0;
                    foreach (var value in Values(input))
                    {
                        total += value;
                        break;
                    }
                    return total + _finallyCount;
                }

                private static IEnumerable<int> Values(int input)
                {
                    try
                    {
                        yield return input;
                        yield return 100;
                    }
                    finally
                    {
                        _finallyCount++;
                    }
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "IteratorFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(42, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerLowersYieldBreakAndRunsIteratorFinally()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "YieldBreakFixture",
            """
            using System.Collections.Generic;

            namespace YieldBreakFixture;

            public static class EntryPoint
            {
                private static int _finallyCount;

                public static int Run(int input)
                {
                    _finallyCount = 0;
                    var total = 0;
                    foreach (var value in Values(input)) total += value;
                    return total + _finallyCount;
                }

                private static IEnumerable<int> Values(int input)
                {
                    try
                    {
                        yield return input;
                        if (input > 0) yield break;
                        yield return 100;
                    }
                    finally
                    {
                        _finallyCount++;
                    }
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "YieldBreakFixture.EntryPoint",
            "Run",
            []));

        var disposeSites = result.Program.DispatchCallSites.Values
            .Where(site => site.Declaration.Definition.Name == "Dispose")
            .ToArray();
        Assert.NotEmpty(disposeSites);
        Assert.All(disposeSites, site => Assert.All(site.Targets, target =>
            Assert.Equal(
                "System.IDisposable.Dispose",
                target.Method.Definition.Name)));

        Assert.Equal(42, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerCompletesYieldBreakBeforeEnumeratorDisposal()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "YieldBreakCompletionFixture",
            """
            using System.Collections.Generic;

            namespace YieldBreakCompletionFixture;

            public static class EntryPoint
            {
                private static int _finallyCount;

                public static int Run(int input)
                {
                    _finallyCount = 0;
                    var iterator = Values(input).GetEnumerator();
                    var first = iterator.MoveNext() ? iterator.Current : -1000;
                    var completed = iterator.MoveNext() ? 100 : 0;
                    return first * 100 + completed * 10 + _finallyCount;
                }

                private static IEnumerable<int> Values(int input)
                {
                    try
                    {
                        yield return input;
                        if (input > 0) yield break;
                        yield return 100;
                    }
                    finally
                    {
                        _finallyCount++;
                    }
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "YieldBreakCompletionFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(4101, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }
}
