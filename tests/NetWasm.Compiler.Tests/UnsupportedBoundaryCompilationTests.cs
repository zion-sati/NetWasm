using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class UnsupportedBoundaryCompilationTests
{
    [Fact]
    public void DynamicBindingIsAbsentFromThePlatformSurface()
    {
        using var assets = TestAssets.Create();

        var exception = Assert.Throws<RoslynCompilationException>(() =>
            assets.CompileSource(
                "DynamicFixture",
                """
                public static class EntryPoint
                {
                    public static int Run(dynamic value) => value.Run();
                }
                """));

        // DynamicAttribute is part of the platform surface, but the C# runtime binder is not, so a
        // dynamic operation still fails to compile.
        Assert.Equal(["CS0656"], exception.DiagnosticCodes);
    }

    [Fact]
    public void ManagedThreadStartIsACatchablePlatformBoundary()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "ThreadFixture",
            """
            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var started = false;
                    try
                    {
                        new System.Threading.Thread(() => started = true).Start();
                    }
                    catch (System.PlatformNotSupportedException)
                    {
                        return started ? 2 : input;
                    }

                    return 1;
                }
            }
            """);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "EntryPoint",
            "Run",
            []));

        // The standard Thread surface binds; creating a native thread throws without
        // running the callback, because the platform has a single managed reactor.
        Assert.Equal(42, CompilerTestSupport.ExecuteWithNode(result.ApplicationModule, assets.Directory, 42));
    }

    [Fact]
    public void BoundedExpressionTreesArePresentOnThePlatformSurface()
    {
        using var assets = TestAssets.Create();

        var assembly = assets.CompileSource(
            "ExpressionTreeFixture",
            """
            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    System.Linq.Expressions.Expression<System.Func<int, int>> expression =
                        value => value + 1;
                    return expression.Compile()(input);
                }
            }
            """);

        Assert.True(File.Exists(assembly));
    }

    [Fact]
    public void QuerySyntaxWithoutTheOptionalLinqLibraryFailsAtRoslynBoundary()
    {
        using var assets = TestAssets.Create();

        var exception = Assert.Throws<RoslynCompilationException>(() =>
            assets.CompileSource(
                "QuerySyntaxFixture",
                """
                public static class EntryPoint
                {
                    public static int Run(int input)
                    {
                        var values = new[] { input, 1 };
                        var query = from value in values where value > 0 select value;
                        return input;
                    }
                }
                """));

        Assert.Equal(["CS1935"], exception.DiagnosticCodes);
    }

    // Compilation controls, not runtime qualification: each removes only the
    // excluded requirement while retaining the surrounding language construct.
    [Theory]
    [InlineData("StaticBinding", "return Increment(input);")]
    [InlineData("SynchronousDelegate", "System.Action action = () => { }; action(); return input;")]
    [InlineData("DelegateLambda", "System.Func<int, int> operation = value => value + 1; return operation(input);")]
    [InlineData("ArrayIteration", "var values = new[] { input, 1 }; var sum = 0; foreach (var value in values) { if (value > 0) sum += value; } return sum;")]
    [InlineData("ManagedFunctionPointer", "delegate* managed<int, int> operation = &Increment; return operation(input);")]
    public void AdjacentSupportedConstructCompilesAgainstTheSamePlatform(string name, string body)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileUnsafeSource(
            name,
            $$"""
            public static unsafe class EntryPoint
            {
                public static int Run(int input) { {{body}} }
                private static int Increment(int value) => value + 1;
            }
            """);

        foreach (var target in new[] { WasmTarget.Wasm32, WasmTarget.Wasm64 })
        {
            var result = NetWasmCompiler.Compile(new CompilerOptions(
                assembly,
                [assets.CoreLib],
                "EntryPoint",
                "Run",
                [],
                Target: target));

            Assert.NotEmpty(result.ApplicationModule);
        }
    }
}
