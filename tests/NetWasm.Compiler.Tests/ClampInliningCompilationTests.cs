using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// Math.Clamp is aggressively inlined, as .NET's is: its throw stays in a helper,
// so a loop that clamps calls nothing on the normal path.
public sealed partial class ClampInliningCompilationTests
{
    private const string Source = """
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var checksum = 0;
                var weight = 0.0;
                var single = 0f;
                long wide = 0;
                for (var i = -40; i < 80 + input; i++)
                {
                    checksum = checksum * 31 + System.Math.Clamp(i, -10 + input, 30);
                    weight += System.Math.Clamp(i / 7.0, -2.5, 4.25 + input);
                    single += System.Math.Clamp(i * 0.5f, -3f, 9f);
                    wide += System.Math.Clamp((long)i * 1000003, -5000000L, 70000000L);
                }
                var thrown = 0;
                try { _ = System.Math.Clamp(1.0, 2.0 + input, 0.0); }
                catch (System.ArgumentException) { thrown |= 1; }
                try { _ = System.Math.Clamp(input, 3, 1); }
                catch (System.ArgumentException) { thrown |= 2; }
                return checksum ^ (int)(weight * 16) ^ (int)(single * 4) ^ (int)(wide % 1000007) ^ thrown << 28;
            }
        }
        """;

    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    public void SpeedBuildsInlineClamp(WasmTarget target, FinalWasmOptimization preset)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("ClampInlining", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Target: target, Optimization: preset));

        if (preset == FinalWasmOptimization.O3)
        {
            var module = Path.Combine(assets.Directory, "application.wasm");
            File.WriteAllBytes(module, result.ApplicationModule);
            using var services = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
            var printed = services.GetRequiredService<IWasmTools>().Run("print", module);
            Assert.True(printed.ExitCode == 0, printed.StandardError);
            Assert.DoesNotMatch(ClampCall(), printed.StandardOutput);
        }

        // As desktop .NET computes them, including both rejected ranges.
        foreach (var (input, expected) in new[] { (0, -1307535501), (1, 583410650), (7, -1611793357) })
        {
            Assert.Equal(expected,
                ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, input, target, result.StaticDataEnd));
        }
    }

    // AggressiveInlining is honored at O1, O2, O3 and Oz; Os ignores it.
    [Theory]
    [InlineData(FinalWasmOptimization.O1, true)]
    [InlineData(FinalWasmOptimization.Oz, true)]
    [InlineData(FinalWasmOptimization.Os, false)]
    public void ClampInlinesWhereverAggressiveInliningIsHonored(FinalWasmOptimization preset, bool inlined)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("ClampPolicy", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Optimization: preset));

        var module = Path.Combine(assets.Directory, "application.wasm");
        File.WriteAllBytes(module, result.ApplicationModule);
        using var services = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
        var printed = services.GetRequiredService<IWasmTools>().Run("print", module);
        Assert.True(printed.ExitCode == 0, printed.StandardError);
        Assert.Equal(!inlined, ClampCall().IsMatch(printed.StandardOutput));
        Assert.Equal(-1307535501,
            ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 0, WasmTarget.Wasm32, result.StaticDataEnd));
    }

    [GeneratedRegex(@"call \$System\.Math::Clamp")]
    private static partial Regex ClampCall();
}
