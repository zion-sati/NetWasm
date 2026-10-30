using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// A loop over an array proves its index in range once its guard is the only
// way in. Code a rewrite leaves unreachable inside the loop does not count as
// another way in.
public sealed class LoopBoundsProofCompilationTests
{
    // `values[index] is 1 or 3 ...` becomes a bit test, and the switch it
    // replaces leaves unreachable code in the loop body.
    private const string MembershipSource = """
        public sealed class Data { public int[] Integers = []; }

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var values = new int[96 + input];
                for (var i = 0; i < values.Length; i++) values[i] = (i * 7 + input) % 19;
                return (int)Count(new Data { Integers = values });
            }

            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
            private static long Count(Data data)
            {
                var values = data.Integers;
                long checksum = 0;
                for (var index = 0; index < values.Length; index++)
                    checksum += values[index] is 1 or 3 or 5 or 7 or 9 or 11 or 13 or 15 ? index : 1;
                return checksum;
            }
        }
        """;

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ALoopWithUnreachableCodeKeepsItsBoundsProof(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("LoopMembership", MembershipSource);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Target: target, Optimization: FinalWasmOptimization.O3));

        // The bounds check is the loop's only unsigned at-least comparison.
        Assert.DoesNotContain("i32.ge_u", Function(assets, result, "EntryPoint::Count"));
        foreach (var (input, expected) in new[] { (0, 1961), (1, 1996), (5, 2142) })
        {
            Assert.Equal(expected,
                ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, input, target, result.StaticDataEnd));
        }
    }

    private static string Function(TestAssets assets, CompilationResult result, string name)
    {
        var module = Path.Combine(assets.Directory, "application.wasm");
        File.WriteAllBytes(module, result.ApplicationModule);
        using var services = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
        var printed = services.GetRequiredService<IWasmTools>().Run("print", module);
        Assert.True(printed.ExitCode == 0, printed.StandardError);
        var function = Regex.Match(printed.StandardOutput,
            $@"\(func \${Regex.Escape(name)}\b.*?(?=\n  \(func |\n  \(type |\z)", RegexOptions.Singleline);
        Assert.True(function.Success, $"{name} was not emitted.");
        return function.Value;
    }
}
