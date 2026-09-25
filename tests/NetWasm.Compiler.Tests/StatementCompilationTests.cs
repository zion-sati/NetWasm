using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class StatementCompilationTests
{
    [Fact]
    public void CompilerBreakSkipsTheRemainderOfAPostTestLoopBody()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "PostTestBreakFixture",
            """
            namespace PostTestBreakFixture;
            public static class EntryPoint
            {
                private static int _trace;
                public static int Run(int input)
                {
                    _trace = 1;
                    var total = input;
                    var count = (input & 3) + 1;
                    var index = 0;
                    do
                    {
                        var current = index++;
                        var stop = ((current + input) & 1) == 0 ? true : false;
                        if (stop) { total += 17; break; }
                        total += current + 5;
                    }
                    while (index < count);
                    return Finish(total + 29);
                }

                private static int Finish(int value)
                {
                    _trace = unchecked(_trace * 31 + value);
                    return value;
                }

                public static int Trace() => _trace;
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "PostTestBreakFixture.EntryPoint",
            "Run",
            [new RequestedExport("trace", "PostTestBreakFixture.EntryPoint", "Trace")]));

        Assert.Equal(48, ExecuteWithNode(
            result.ApplicationModule, assets.Directory, -3));
    }

    [Fact]
    public void CompilerPreservesLoopsSwitchAndGotoCaseControlFlow()
    {
        using var assets = TestAssets.Create();
        const string source = """
            namespace StatementControlFlowFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var value = 0;
                    for (var index = 0; index < 4; index++)
                    {
                        if (index == 1)
                        {
                            continue;
                        }
                        value += index;
                    }

                    do
                    {
                        value++;
                    }
                    while (value < 6);

                    while (true)
                    {
                        value++;
                        break;
                    }

                    switch (input)
                    {
                        case 40:
                            value += 1;
                            goto case 41;
                        case 41:
                            value += 10;
                            goto default;
                        default:
                            return value + 24;
                    }
                }
            }
            """;

        foreach (var optimize in new[] { false, true })
        {
            var assembly = optimize
                ? assets.CompileOptimizedSource("StatementControlFlowOptimized", source)
                : assets.CompileSource("StatementControlFlowDebug", source);
            var result = NetWasmCompiler.Compile(new CompilerOptions(
                assembly,
                [assets.CoreLib],
                "StatementControlFlowFixture.EntryPoint",
                "Run",
                []));

            Assert.Equal(42, ExecuteWithNode(result.ApplicationModule, assets.Directory, 40));
            Assert.Equal(41, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
        }
    }

    [Fact]
    public void CompilerDisposesUsingDeclarationsOnReturnAndException()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "UsingStatementFixture",
            """
            namespace UsingStatementFixture;

            public sealed class Resource : System.IDisposable
            {
                public static int Disposals;
                public void Dispose() => Disposals++;
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    Resource.Disposals = 0;
                    try
                    {
                        using var outer = new Resource();
                        using (var inner = new Resource())
                        {
                            if (input < 0)
                            {
                                throw new System.ArgumentException();
                            }
                            return input + 1;
                        }
                    }
                    catch (System.ArgumentException)
                    {
                        return Resource.Disposals * 10;
                    }
                    finally
                    {
                        Resource.Disposals++;
                    }
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "UsingStatementFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(42, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
        Assert.Equal(20, ExecuteWithNode(result.ApplicationModule, assets.Directory, -1));
    }
}
