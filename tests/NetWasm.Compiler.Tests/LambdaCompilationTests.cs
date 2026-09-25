using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class LambdaCompilationTests
{
    [Fact]
    public void CompilerExecutesNonCapturingCapturingAndNestedLambdas()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "LambdaFixture",
            """
            namespace LambdaFixture;

            public sealed class Target
            {
                private readonly int _offset;

                public Target(int offset) => _offset = offset;

                public int Run(int input)
                {
                    System.Func<int, int> captureThis = value => value + _offset;
                    return captureThis(input);
                }
            }

            public static class EntryPoint
            {
                private static System.Func<int, int> Wrap(int offset)
                {
                    System.Func<int, int> outer = value =>
                    {
                        System.Func<int, int> inner = nested => nested + offset;
                        return inner(value);
                    };
                    return outer;
                }

                public static int Run(int input)
                {
                    System.Func<int, int> nonCapturing = static value => value + 1;
                    var shared = 1;
                    System.Func<int> read = () => shared;
                    System.Action increment = () => shared++;
                    increment();
                    var target = new Target(3);
                    return nonCapturing(input) + read() + target.Run(input)
                        + Wrap(4)(input);
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "LambdaFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(133, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerExecutesClosedGenericLambdaSpecializations()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "GenericLambdaFixture",
            """
            namespace GenericLambdaFixture;

            public static class EntryPoint
            {
                private static T Apply<T>(T value)
                {
                    System.Func<T, T> identity = item => item;
                    return identity(value);
                }

                public static int Run(int input)
                {
                    var text = Apply("ok");
                    return Apply(input) + text.Length;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "GenericLambdaFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(43, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerPropagatesManagedExceptionsFromLambda()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "ThrowingLambdaFixture",
            """
            namespace ThrowingLambdaFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    System.Func<int> operation = () => throw new System.ArgumentException();
                    try
                    {
                        return operation();
                    }
                    catch (System.ArgumentException)
                    {
                        return input + 1;
                    }
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "ThrowingLambdaFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(42, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerTranslatesAsyncLambdaWithSynchronousCompletion()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "AsyncLambdaFixture",
            """
            using System.Threading.Tasks;

            namespace AsyncLambdaFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    System.Func<int, Task<int>> operation = async value => value + 1;
                    return operation(input).Result;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(CompilerTestSupport.CreateReactorOptions(
            assets,
            assembly,
            "AsyncLambdaFixture.EntryPoint",
            "Run"));

        Assert.NotEmpty(result.ApplicationModule);
        Assert.Contains(result.Program.MethodInstances.Values,
            method => method.Definition.Name == "AwaitUnsafeOnCompleted" ||
                      method.Definition.Name == "Start");
    }
}
