using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// Upstream code reaches small helpers through long chains of AggressiveInlining
// calls: ordinal ignore-case search reads CharUnicodeInfo's table getters six
// calls down. Here a table getter sits five calls below the loop. The general
// rounds stop three calls down, and the small-callee rounds past them inline the
// rest, so the loop makes no call and reads the table's RVA data directly.
public sealed partial class DeepSmallCalleeInliningCompilationTests
{
    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;
        using System.Runtime.InteropServices;

        public static class EntryPoint
        {
            private static ReadOnlySpan<byte> Table => new byte[] { 3, 1, 4, 1, 5, 9, 2, 6, 5, 3, 5, 8, 9, 7, 9, 3 };

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static int Read(int index) => Unsafe.Add(ref MemoryMarshal.GetReference(Table), index & 15);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static int Level4(int value) => Read(value) + 1;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static int Level3(int value) => Level4(value ^ 5) * 2;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static int Level2(int value) => Level3(value + 3) - value;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static int Level1(int value) => Level2(value) ^ Level2(value + 1);

            public static int Run(int input)
            {
                var sum = 0;
                for (var i = 0; i < 256 + input; i++) sum = unchecked(sum * 31 + Level1(i));
                return sum;
            }
        }
        """;

    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    public void SpeedBuildsInlineSmallCalleesBelowTheGeneralDepth(WasmTarget target, FinalWasmOptimization preset)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("DeepSmallCalleeInlining", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Target: target, Optimization: preset));

        if (preset == FinalWasmOptimization.O3)
        {
            var module = Path.Combine(assets.Directory, "application.wasm");
            File.WriteAllBytes(module, result.ApplicationModule);
            using var services = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
            var printed = services.GetRequiredService<IWasmTools>().Run("print", module);
            Assert.True(printed.ExitCode == 0, printed.StandardError);
            Assert.DoesNotMatch(HelperCall(), printed.StandardOutput);
        }

        // The checksums, as desktop .NET computes them.
        foreach (var (input, expected) in new[] { (0, 1253770176), (1, 212169797), (7, -625300425) })
        {
            Assert.Equal(expected,
                ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, input, target, result.StaticDataEnd));
        }
    }

    [GeneratedRegex(@"call \$EntryPoint::(Level[1-4]|Read|get_Table)\b")]
    private static partial Regex HelperCall();
}
