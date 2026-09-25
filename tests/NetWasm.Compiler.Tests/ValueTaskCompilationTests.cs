using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class ValueTaskCompilationTests
{
    [Fact]
    public void CompilerCompilesCompletedAndTaskBackedValueTasks()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "ValueTaskFixture",
            """
            using System.Threading.Tasks;

            namespace ValueTaskFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var completed = new ValueTask<int>(input);
                    var backed = new ValueTask<int>(Task<int>.FromResult(1));
                    var empty = default(ValueTask);
                    return completed.Result + backed.Result
                        + (completed.IsCompletedSuccessfully ? 1 : 0)
                        + (backed.AsTask().Result == 1 ? 1 : 0)
                        + (empty.IsCompletedSuccessfully ? 1 : 0);
                }
            }
            """);

        var result = NetWasmCompiler.Compile(CreateReactorOptions(
            assets,
            assembly,
            "ValueTaskFixture.EntryPoint",
            "Run"));

        Assert.Contains(result.Program.ConstructedTypes, type =>
            type.CanonicalName.Contains("ValueTask`1", StringComparison.Ordinal));
        Assert.NotEmpty(result.ApplicationModule);
    }

    [Fact]
    public void CompilerCompilesAsyncValueTaskBuilders()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "AsyncValueTaskFixture",
            """
            using System.Threading.Tasks;

            namespace AsyncValueTaskFixture;

            public static class EntryPoint
            {
                private static async ValueTask<int> AddAsync(int value)
                {
                    await Task.Yield();
                    return value + 1;
                }

                private static async ValueTask CompleteAsync()
                {
                    await Task.Yield();
                }

                public static int Run(int input)
                {
                    _ = AddAsync(input);
                    _ = CompleteAsync();
                    return input;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(CreateReactorOptions(
            assets,
            assembly,
            "AsyncValueTaskFixture.EntryPoint",
            "Run"));

        Assert.Contains(result.Program.MethodInstances.Values, method =>
            method.Definition.Name == "Create" &&
            method.DeclaringType.CanonicalName.Contains(
                "AsyncValueTaskMethodBuilder", StringComparison.Ordinal));
        Assert.NotEmpty(result.ApplicationModule);
    }
}
