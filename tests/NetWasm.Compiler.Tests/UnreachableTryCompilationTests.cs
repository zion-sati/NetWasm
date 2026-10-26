using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// Instantiated for the type its first branch tests, a generic method returns
// before its try, as double.CreateChecked<double> does under LINQ's Sum over
// double[]. The try had no exception group but kept its leave, and O3's scalar
// call lifetime planner rejected the leave as outside any region (NW1002).
public sealed class UnreachableTryCompilationTests
{
    private const string Source = """
        public static class EntryPoint
        {
            private static int s_finally;

            public static int Run(int input)
            {
                var values = new[] { 1.5, 2, input };
                var sum = 0.0;
                long count = 0;
                foreach (var value in values)
                {
                    sum = checked(sum + Convert<double>(value));
                    count = checked(count + Count<long>(1));
                }
                return (int)(sum * 2) + (int)count * 100 + s_finally;
            }

            private static double Convert<TOther>(TOther value) where TOther : System.Numerics.INumberBase<TOther>
            {
                if (typeof(TOther) == typeof(double)) return (double)(object)value;
                try { return System.Convert.ToDouble((object?)value); }
                catch { throw new System.NotSupportedException(); }
            }

            private static long Count<T>(T value) where T : System.Numerics.INumberBase<T>
            {
                if (typeof(T) == typeof(long)) return (long)(object)value;
                try { return System.Convert.ToInt64((object?)value); }
                finally { s_finally++; }
            }
        }
        """;

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void SpeedBuildsCompileAGenericMethodWhoseTryIsUnreachable(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("UnreachableTry", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Target: target, Optimization: FinalWasmOptimization.O3));

        foreach (var (input, expected) in new[] { (0, 307), (3, 313) })
        {
            Assert.Equal(expected,
                ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, input, target, result.StaticDataEnd));
        }
    }
}
