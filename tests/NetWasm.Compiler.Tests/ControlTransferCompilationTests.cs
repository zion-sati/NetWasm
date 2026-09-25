using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class ControlTransferCompilationTests
{
    [Theory]
    [InlineData(-3, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(3, 0)]
    public void CompilerPreservesUnsignedRangeBranchSemantics(int input, int expected)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "UnsignedRangeBranchFixture",
            """
            namespace UnsignedRangeBranchFixture;

            public static class EntryPoint
            {
                public static int Run(int input) =>
                    input == -3 || (uint)(input - 1) <= 1u ? 1 : 0;
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "UnsignedRangeBranchFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(expected, ExecuteWithNode(
            result.ApplicationModule,
            assets.Directory,
            input));
    }

    [Fact]
    public void CompilerLowersForwardAndBackwardGotoLabels()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "GotoFixture",
            """
            namespace GotoFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var value = 0;
                    goto Enter;

                Repeat:
                    value += 2;
                    if (value < 7) goto Repeat;
                    goto Exit;

                Enter:
                    value = input;
                    goto Repeat;

                Exit:
                    return value;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "GotoFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(7, ExecuteWithNode(result.ApplicationModule, assets.Directory, 1));
    }

    [Fact]
    public void CompilerRunsFinallyExactlyOnceWhenGotoLeavesTry()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "GotoFinallyFixture",
            """
            namespace GotoFinallyFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var value = input;
                    try
                    {
                        if (input > 0) goto Exit;
                        value = -100;
                    }
                    finally
                    {
                        value += 10;
                    }

                Exit:
                    return value;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "GotoFinallyFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(11, ExecuteWithNode(result.ApplicationModule, assets.Directory, 1));
        Assert.Equal(-90, ExecuteWithNode(result.ApplicationModule, assets.Directory, 0));
    }

    [Fact]
    public void CompilerExecutesShortCircuitPostTestContinueInDebugAndReleaseCil()
    {
        using var assets = TestAssets.Create();
        var source =
            """
            namespace ShortCircuitPostTestFixture;

            public static class EntryPoint
            {
                private static uint _next;

                public static int Run(int input)
                {
                    _next = 0;
                    do
                    {
                        _next++;
                    }
                    while (_next == 0 || Contains(_next, input));

                    return (int)_next;
                }

                private static bool Contains(uint value, int limit) => value < (uint)limit;
            }
            """;
        var assemblies = new[]
        {
            assets.CompileSource("ShortCircuitPostTestDebugFixture", source),
            assets.CompileOptimizedSource("ShortCircuitPostTestReleaseFixture", source),
        };

        foreach (var assembly in assemblies)
        {
            var result = NetWasmCompiler.Compile(new CompilerOptions(
                assembly,
                [assets.CoreLib],
                "ShortCircuitPostTestFixture.EntryPoint",
                "Run",
                []));

            Assert.Equal(3, ExecuteWithNode(result.ApplicationModule, assets.Directory, 3));
        }
    }
}
