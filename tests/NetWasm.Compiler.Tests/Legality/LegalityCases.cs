using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests.Legality;

using static CompilerTestSupport;

// An optimization's legality tests: programs that break, one at a time, a
// condition under which the optimization must not fire, compiled with the
// optimization on and off, each giving desktop .NET 11's results.
internal static class LegalityCases
{
    public static void Run(
        string name,
        string source,
        (int Input, int Expected)[] cases,
        FinalWasmOptimization optimization,
        WasmOptimizationOverrides? overrides = null,
        bool allowUnsafe = false)
    {
        using var assets = TestAssets.Create();
        var assembly = allowUnsafe
            ? assets.CompileOptimizedUnsafeSource(name, source)
            : assets.CompileOptimizedSource(name, source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Optimization: optimization, OptimizationOverrides: overrides));
        foreach (var (input, expected) in cases)
        {
            Assert.Equal(expected, ExecuteWithStandardWasiNode(
                result.ApplicationModule, assets.Directory, input, WasmTarget.Wasm32, result.StaticDataEnd));
        }
    }
}
