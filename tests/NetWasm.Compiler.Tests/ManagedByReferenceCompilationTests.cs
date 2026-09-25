using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class ManagedByReferenceCompilationTests
{
    [Fact]
    public void CompilerPreservesScalarReferenceLocalsArgumentsAndReturns()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "ManagedByReferenceFixture",
            """
            namespace ManagedByReferenceFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var left = input;
                    var right = 1;
                    ref var selected = ref Select(ref left, ref right, input != 0);
                    Increment(ref selected);
                    return left * 10 + right;
                }

                private static ref int Select(ref int left, ref int right, bool first)
                {
                    if (first)
                    {
                        return ref left;
                    }
                    return ref right;
                }

                private static void Increment(ref int value) => value++;
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "ManagedByReferenceFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(421, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
        Assert.Equal(2, ExecuteWithNode(result.ApplicationModule, assets.Directory, 0));
    }

    [Fact]
    public void CompilerEmitsManagedReferencesForBothAddressWidths()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "ManagedByReferenceWidthFixture",
            """
            namespace ManagedByReferenceWidthFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var value = input;
                    return Read(ref value);
                }

                private static int Read(ref int value) => value;
            }
            """);

        foreach (var target in new[] { WasmTarget.Wasm32, WasmTarget.Wasm64 })
        {
            var result = NetWasmCompiler.Compile(new CompilerOptions(
                assembly,
                [assets.CoreLib],
                "ManagedByReferenceWidthFixture.EntryPoint",
                "Run",
                [],
                target));

            Assert.NotEmpty(result.ApplicationModule);
            Assert.Equal(target, result.Layouts.Target.Target);
        }
    }

    [Fact]
    public void CompilerPreservesFieldArrayAndReadonlyReferences()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "ManagedInteriorReferenceFixture",
            """
            namespace ManagedInteriorReferenceFixture;

            public sealed class Holder
            {
                public int Value;
            }

            public static class EntryPoint
            {
                public static int Field(int input)
                {
                    var holder = new Holder { Value = input };
                    ref var field = ref holder.Value;
                    field++;
                    return field;
                }

                public static int Array(int input)
                {
                    var values = new int[] { 1, input };
                    ref var element = ref values[1];
                    element++;
                    return element;
                }

                public static int Readonly(int input)
                {
                    var values = new int[] { input };
                    ref readonly var readonlyElement = ref values[0];
                    return readonlyElement;
                }

                public static int Run(int input)
                {
                    var holder = new Holder { Value = input };
                    ref var field = ref holder.Value;
                    var values = new int[] { 1, 2 };
                    ref var element = ref values[1];
                    field++;
                    element += field;
                    ref readonly var readonlyElement = ref values[0];
                    return field * 100 + element * 10 + readonlyElement;
                }
            }
            """);

        Assert.Equal(42, CompileAndRun("Field"));
        Assert.Equal(42, CompileAndRun("Array"));
        Assert.Equal(41, CompileAndRun("Readonly"));
        Assert.Equal(4641, CompileAndRun("Run"));

        int CompileAndRun(string method)
        {
            var result = NetWasmCompiler.Compile(new CompilerOptions(
                assembly,
                [assets.CoreLib],
                "ManagedInteriorReferenceFixture.EntryPoint",
                method,
                []));
            return ExecuteWithNode(result.ApplicationModule, assets.Directory, 41);
        }
    }
}
