using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class NullableBoxingCompilationTests
{
    [Fact]
    public void CompilerExecutesNullableBoxAndUnboxAnyContracts()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "NullableBoxingFixture",
            """
            namespace NullableBoxingFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    int? populated = input;
                    int? empty = null;
                    if (!populated.HasValue) return -7;
                    if (populated.Value != input) return -8;
                    object populatedBox = populated;
                    object? emptyBox = empty;
                    if (populatedBox is null) return -4;
                    if ((int)populatedBox != input) return -5;
                    if (emptyBox is not null) return -6;
                    int? populatedResult = (int?)populatedBox;
                    int? emptyResult = (int?)emptyBox;
                    if (!populatedResult.HasValue) return -1;
                    if (populatedResult.Value != input) return -2;
                    if (emptyResult.HasValue) return -3;
                    return input;
                }
            }
            """);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "NullableBoxingFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(41, ExecuteWithNode(
            compilation.ApplicationModule,
            assets.Directory,
            41));
    }
}
