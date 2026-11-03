using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class GenericArgumentDescriptorCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void ExistingGenericArgumentApiWithExpressionMetadata(bool includeExpression, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var source = """
            using System;
            using System.Linq.Expressions;
            namespace ExistingGenericArguments;
            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var arguments = typeof(Pair<int, string>).GetGenericArguments();
                    EXPRESSION
                    return arguments.Length == 2 && arguments[0] == typeof(int) && arguments[1] == typeof(string) ? input + 1 : 1;
                }
                public static int Increment(int value) => value + 1;
                private sealed class Pair<T, U> { }
            }
            """.Replace("EXPRESSION", includeExpression
                ? "Expression<Func<int, int>> expression = value => Increment(value); var method = ((MethodCallExpression)expression.Body).Method; if (method.GetParameters()[0].ParameterType != typeof(int) || expression.Compile()(41) != 42) return 2;"
                : "");
        var assembly = assets.CompileSource("ExistingGenericArguments", source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "ExistingGenericArguments.EntryPoint",
            "Run", [new("run", "ExistingGenericArguments.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform@1.0.0/async-platform"));
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

}
