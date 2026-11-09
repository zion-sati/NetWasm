using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// Static data starts above the first KiB, which nothing then uses, so wasm-opt may assume it
// is unused (--low-memory-unused) and fold constant address adds into load and store offsets.
// A program's statics, string literals and type data all sit at or above Binaryen's 1024-byte
// bound, and the program still runs from there.
public sealed class LowMemoryLayoutCompilationTests
{
    private const int LowMemoryBound = 1024;

    private const string Source = """
        public static class EntryPoint
        {
            private static int s_counter = 3;
            private static readonly string s_name = "statics";
            private static readonly int[] s_values = [5, 7, 11];

            public static int Run(int input)
            {
                s_counter += input;
                var sum = 0;
                foreach (var value in s_values) sum += value;
                return s_counter + sum + s_name.Length + "literal".Length;
            }
        }
        """;

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void NothingIsPlacedInTheFirstKiBOfMemory(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("LowMemoryLayout", Source);

        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target,
            Optimization: FinalWasmOptimization.O3));

        Assert.NotEmpty(compilation.Layouts.DataSegments);
        Assert.All(compilation.Layouts.DataSegments, segment => Assert.True(segment.Address >= LowMemoryBound,
            $"a data segment starts at {segment.Address}, below {LowMemoryBound}"));
        Assert.True(compilation.StaticDataEnd > LowMemoryBound);
        Assert.Equal(3 + 4 + 23 + 7 + 7, ExecuteWithStandardWasiNode(
            compilation.ApplicationModule, assets.Directory, 4, target, compilation.StaticDataEnd));
    }
}
