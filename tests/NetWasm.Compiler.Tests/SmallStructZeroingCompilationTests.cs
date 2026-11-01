using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// initobj, default(T), a struct's newobj and unbox.any to Nullable<T> zero the
// value first. Wasmtime runs memory.fill out of line, so a Memory<T> slice paid
// a call to clear 12 bytes; values of up to 64 bytes are zeroed with stores, and
// larger ones still fill.
public sealed partial class SmallStructZeroingCompilationTests
{
    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;

        public struct Pair
        {
            public long A;
            public int B;
            public Pair(int b) { A = 1; B = b; }
        }

        public struct Odd { public byte A; public short B; public byte C; }

        public struct Wide { public long A, B, C, D, E, F, G, H, I, J; }

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var pair = MakePair(input);
                var odd = MakeOdd(input);
                var wide = MakeWide(input);
                var unboxed = Unbox(input == 0 ? null : input + 1);
                return (int)pair.A + pair.B + odd.A + odd.B + odd.C + (int)wide.J + unboxed.GetValueOrDefault();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static Pair MakePair(int input) => new Pair(input);

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static Odd MakeOdd(int input) => input > 5 ? default : new Odd { C = (byte)input };

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static Wide MakeWide(int input)
            {
                var wide = default(Wide);
                wide.J = input * 2;
                return wide;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int? Unbox(object? value) => (int?)value;
        }
        """;

    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    public void SmallValuesZeroWithStores(WasmTarget target, FinalWasmOptimization preset)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("SmallStructZeroing", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Target: target, Optimization: preset));

        var module = Path.Combine(assets.Directory, "application.wasm");
        File.WriteAllBytes(module, result.ApplicationModule);
        using var services = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
        var printed = services.GetRequiredService<IWasmTools>().Run("print", module);
        Assert.True(printed.ExitCode == 0, printed.StandardError);
        foreach (var method in new[] { "MakePair", "MakeOdd", "Unbox" })
        {
            var body = Function().Matches(printed.StandardOutput)
                .Single(function => function.Groups["name"].Value == $"EntryPoint::{method}").Value;
            Assert.DoesNotContain("memory.fill", body);
        }

        // As desktop .NET computes them: Pair's A is 1, Odd keeps the input up
        // to 5, Wide's J doubles it, and the unboxed value is input + 1.
        foreach (var (input, expected) in new[] { (0, 1), (3, 1 + 3 + 3 + 6 + 4), (7, 1 + 7 + 14 + 8) })
        {
            Assert.Equal(expected,
                ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, input, target, result.StaticDataEnd));
        }
    }

    [GeneratedRegex(@"\(func \$(?<name>\S+)[\s\S]*?(?=\n  \(func |\n  \(export |\z)")]
    private static partial Regex Function();
}
