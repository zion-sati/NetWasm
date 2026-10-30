using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// A rasterizer's edge function reads its struct arguments through property
// getters. O3 reads the getters as field loads, so the edge function is a
// straight-line leaf the triangle loop inlines, and the inlined body reads each
// struct where the triangle method received it instead of from a copy.
public sealed partial class ValueArgumentInliningCompilationTests
{
    private const string Source = """
        public readonly struct Screen
        {
            public Screen(double x, double y, double invZ)
            {
                X = x;
                Y = y;
                InvZ = invZ;
            }

            public double X { get; }
            public double Y { get; }
            public double InvZ { get; }
        }

        public static class EntryPoint
        {
            private static double Edge(Screen a, Screen b, double x, double y) => (x - a.X) * (b.Y - a.Y) - (y - a.Y) * (b.X - a.X);

            public static int Run(int input) =>
                Fill(new Screen(1 + input, 2, 0.5), new Screen(40, 3 + input, 0.25), new Screen(7, 30, 0.125));

            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
            private static int Fill(Screen a, Screen b, Screen c)
            {
                // Edge(a, b, c.X, c.Y), written out: only calls inside the loop are leaf candidates.
                var area = (c.X - a.X) * (b.Y - a.Y) - (c.Y - a.Y) * (b.X - a.X);
                var covered = 0;
                var weight = 0.0;
                for (var y = 0; y < 32; y++)
                {
                    for (var x = 0; x < 48; x++)
                    {
                        var w0 = Edge(b, c, x + .5, y + .5) / area;
                        var w1 = Edge(c, a, x + .5, y + .5) / area;
                        if (w0 < 0 || w1 < 0 || 1 - w0 - w1 < 0) continue;
                        covered++;
                        weight += w0 * a.InvZ + w1 * b.InvZ + (1 - w0 - w1) * c.InvZ;
                    }
                }
                return covered * 1000 + (int)(weight * 10);
            }
        }
        """;

    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    public void SpeedBuildsReadStructArgumentsOfInlinedLeavesInPlace(WasmTarget target, FinalWasmOptimization preset)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("ValueArgumentInlining", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Target: target, Optimization: preset));

        if (preset == FinalWasmOptimization.O3)
        {
            var module = Path.Combine(assets.Directory, "application.wasm");
            File.WriteAllBytes(module, result.ApplicationModule);
            using var services = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
            var printed = services.GetRequiredService<IWasmTools>().Run("print", module);
            Assert.True(printed.ExitCode == 0, printed.StandardError);
            var fill = FillFunction().Match(printed.StandardOutput);
            Assert.True(fill.Success, "EntryPoint::Fill was not emitted.");
            Assert.DoesNotContain("call $EntryPoint::Edge", fill.Value);
            // Neither the call nor the inlined body copies a struct argument.
            Assert.DoesNotContain("memory.copy", fill.Value);
        }

        // As desktop .NET computes them.
        foreach (var (input, expected) in new[] { (0, 546591), (1, 529543), (7, 453314) })
        {
            Assert.Equal(expected,
                ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, input, target, result.StaticDataEnd));
        }
    }

    [GeneratedRegex(@"\(func \$EntryPoint::Fill\b.*?(?=\n  \(func |\n  \(type |\z)", RegexOptions.Singleline)]
    private static partial Regex FillFunction();
}
