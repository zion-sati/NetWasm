using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class PatternCompilationTests
{
    [Fact]
    public void CompilerLowersLogicalPropertyAndSwitchExpressionPatterns()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "PatternFixture",
            """
            namespace PatternFixture;

            public abstract class Node;
            public sealed class Number(int value) : Node
            {
                public int Value { get; } = value;
            }
            public sealed class Empty : Node;

            public static class EntryPoint
            {
                private static int Read(Node node) => node switch
                {
                    Number { Value: >= 0 and < 10 } number when number.Value != 5
                        => number.Value,
                    Number { Value: 5 } => 50,
                    Empty => 1,
                    not null => 2,
                    _ => 3,
                };

                public static int Run(int input)
                {
                    Node node = input >= 0 ? new Number(input) : new Empty();
                    var number = node as Number;
                    var typeScore = node is Number && number != null
                        && node.GetType() == typeof(Number)
                        ? 1
                        : 0;
                    return Read(node) + typeScore;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "PatternFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(3, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
        Assert.Equal(51, ExecuteWithNode(result.ApplicationModule, assets.Directory, 5));
        Assert.Equal(1, ExecuteWithNode(result.ApplicationModule, assets.Directory, -1));
    }
}
