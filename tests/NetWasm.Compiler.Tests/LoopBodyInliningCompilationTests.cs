using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// A shading helper with a clamp branches, so it is no straight-line leaf. Called
// for every pixel, it still belongs in the pixel loop, as .NET's JIT puts it.
public sealed class LoopBodyInliningCompilationTests
{
    private const string ShadingSource = """
        public sealed class Shader(bool fullBright)
        {
            private readonly bool _fullBright = fullBright;

            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
            public int Fill(int[] pixels, int light)
            {
                var checksum = 0;
                for (var index = 0; index < pixels.Length; index++)
                {
                    var color = (int)(index * 2654435761u & 0xFFFFFF);
                    pixels[index] = Shade(color, light, index * .75);
                    checksum = checksum * 31 + pixels[index];
                }
                return checksum;
            }

            private int Shade(int color, int light, double distance)
            {
                double factor = _fullBright ? 1 : System.Math.Clamp(light / 255.0 - distance / 2400, .12, 1);
                return (int)((color >> 16 & 255) * factor) << 16 |
                    (int)((color >> 8 & 255) * factor) << 8 | (int)((color & 255) * factor);
            }
        }

        public static class EntryPoint
        {
            public static int Run(int input) =>
                new Shader(false).Fill(new int[640 + input], 96 + input) ^ new Shader(true).Fill(new int[64], input);
        }
        """;

    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    public void SpeedBuildsInlineABranchingHelperIntoTheLoopThatCallsIt(WasmTarget target, FinalWasmOptimization preset)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("LoopBodyShading", ShadingSource);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Target: target, Optimization: preset));

        if (preset == FinalWasmOptimization.O3)
        {
            var fill = Function(assets, result, "Shader::Fill");
            Assert.DoesNotContain("call $Shader::Shade", fill);
            Assert.DoesNotContain("call $System.Math::Clamp", fill);
        }

        // As desktop .NET computes them.
        foreach (var (input, expected) in new[] { (0, -1820115020), (1, 1560962937), (5, 1037969165) })
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
