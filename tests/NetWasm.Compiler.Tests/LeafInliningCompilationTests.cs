// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// A tuple swap of two array elements is seventeen instructions, one more than
// the small-callee limit. O3 inlines it as a leaf, so the quicksort's partition
// loop keeps its indices in registers instead of calling the helper.
public sealed partial class LeafInliningCompilationTests
{
    private const string Source = """
        public static class EntryPoint
        {
            private static void Swap(int[] values, int a, int b) => (values[a], values[b]) = (values[b], values[a]);

            private static void Sort(int[] values, int low, int high)
            {
                while (low < high)
                {
                    var pivot = values[low + (high - low) / 2];
                    var i = low;
                    var j = high;
                    while (i <= j)
                    {
                        while (values[i] < pivot) i++;
                        while (values[j] > pivot) j--;
                        if (i <= j)
                        {
                            Swap(values, i, j);
                            i++;
                            j--;
                        }
                    }
                    if (j - low < high - i)
                    {
                        Sort(values, low, j);
                        low = i;
                    }
                    else
                    {
                        Sort(values, i, high);
                        high = j;
                    }
                }
            }

            public static int Run(int input)
            {
                var values = new int[64 + input];
                var state = 2463534242u + (uint)input;
                for (var i = 0; i < values.Length; i++)
                {
                    state ^= state << 13;
                    state ^= state >> 17;
                    state ^= state << 5;
                    values[i] = (int)(state % 1000);
                }
                Sort(values, 0, values.Length - 1);
                var checksum = 0;
                for (var i = 0; i < values.Length; i++)
                {
                    if (i > 0 && values[i - 1] > values[i]) return -1;
                    checksum = checksum * 31 + values[i];
                }
                return checksum;
            }
        }
        """;

    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    public void SpeedBuildsInlineTheTupleSwapLeaf(WasmTarget target, FinalWasmOptimization preset)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("LeafInlining", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Target: target, Optimization: preset));

        if (preset == FinalWasmOptimization.O3)
        {
            var module = Path.Combine(assets.Directory, "application.wasm");
            File.WriteAllBytes(module, result.ApplicationModule);
            using var services = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
            var printed = services.GetRequiredService<IWasmTools>().Run("print", module);
            Assert.True(printed.ExitCode == 0, printed.StandardError);
            Assert.DoesNotMatch(SwapCall(), printed.StandardOutput);
        }

        // The checksums of the sorted values, as desktop .NET computes them.
        foreach (var (input, expected) in new[] { (0, -857645765), (1, -557372757), (7, -1108372809) })
        {
            Assert.Equal(expected,
                ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, input, target, result.StaticDataEnd));
        }
    }

    [GeneratedRegex(@"call \$EntryPoint::Swap\b")]
    private static partial Regex SwapCall();
}
