using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// A virtual method's in parameter is a byref the override only reads. Once the
// override is devirtualized and inlined, it reads the caller's local through the
// local's own address.
public sealed class InParameterInliningCompilationTests
{
    private const string Source = """
        public struct Pair
        {
            public int A;
            public int B;
        }

        public abstract class Measure
        {
            public abstract int Total(in Pair pair);
        }

        public sealed class Sum : Measure
        {
            public override int Total(in Pair pair) => pair.A + pair.B;
        }

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var pair = new Pair { A = input, B = input * 10 };
                Measure measure = new Sum();
                var first = measure.Total(in pair);
                pair.B += 1;
                return first * 1000 + measure.Total(in pair);
            }
        }
        """;

    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.None)]
    public void ForwardsTheCallersAddressToAnInlinedInParameter(WasmTarget target, FinalWasmOptimization preset)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource($"InParameterInlining_{target}_{preset}", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target, Optimization: preset));

        // 11 * input then 11 * input + 1.
        foreach (var (input, expected) in new[] { (1, 11012), (3, 33034) })
        {
            Assert.Equal(expected, ExecuteWithStandardWasiNode(
                result.ApplicationModule, assets.Directory, input, target, result.StaticDataEnd));
        }
    }
}
