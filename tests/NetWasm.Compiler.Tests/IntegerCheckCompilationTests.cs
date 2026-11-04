using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// Integer checks that the operands' ranges rule out are not emitted: the
// trial division of #166 divides without checking its divisor or MinValue,
// checked sums and products of bytes and a checked conversion of a masked
// value compute without an overflow check, a handler divides by a constant
// without checking it, a method specialized for its one constant divisor
// divides by it unchecked, and each function keeps its checks
// when the elimination is off. Results match desktop .NET either way.
public sealed class IntegerCheckCompilationTests
{
    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;

        public static class EntryPoint
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static bool IsPrime(int value)
            {
                if (value < 2) return false;
                if ((value & 1) == 0) return value == 2;
                var divisor = 3;
                var quotient = value / divisor;
                while (divisor <= quotient)
                {
                    if (quotient * divisor == value) return false;
                    divisor += 2;
                    quotient = value / divisor;
                }
                return true;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static bool IsPrimeDivRem(int value)
            {
                if (value < 2) return false;
                if ((value & 1) == 0) return value == 2;
                for (var divisor = 3; divisor <= value / divisor; divisor += 2)
                {
                    if (value % divisor == 0) return false;
                }
                return true;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ByteSums(byte a, byte b) => checked(a + b) + checked(a * b);

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Masked(int value) => checked((byte)(value & 0x7F));

            // Every call passes 7, so the method is specialized for it.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Scaled(int value, int divisor)
            {
                if (divisor < 1) return 0;
                return value / divisor;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Fail(int value)
            {
                if (value >= 0) throw new InvalidOperationException();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Handler(int value)
            {
                try
                {
                    Fail(value);
                    return 0;
                }
                catch (InvalidOperationException)
                {
                    return value / 5;
                }
            }

            public static int Run(int input)
            {
                var primes = 0;
                for (var n = 0; n < 300 + input; n++)
                {
                    if (IsPrime(n)) primes++;
                    if (IsPrimeDivRem(n)) primes += 1000;
                }
                return primes + ByteSums((byte)(input * 40), (byte)(255 - input)) * 7 + Masked(input * 1001) * 13 +
                    Handler(input * 77) * 17 + Scaled(input * 1234, 7) * 19;
            }
        }
        """;

    // As desktop .NET computes them.
    private static readonly (int Input, int Expected)[] Cases = [(0, 63847), (1, 140204), (7, 132130), (40, 312134)];

    [Theory]
    [InlineData(WasmTarget.Wasm32, ManagedIntegerCheckElimination.Auto)]
    [InlineData(WasmTarget.Wasm32, ManagedIntegerCheckElimination.Disabled)]
    [InlineData(WasmTarget.Wasm64, ManagedIntegerCheckElimination.Auto)]
    [InlineData(WasmTarget.Wasm64, ManagedIntegerCheckElimination.Disabled)]
    public void ChecksTheOperandsRuleOutAreNotEmitted(WasmTarget target, ManagedIntegerCheckElimination elimination)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource($"IntegerChecks_{target}_{elimination}", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Target: target, Optimization: FinalWasmOptimization.O3,
            OptimizationOverrides: new WasmOptimizationOverrides(IntegerCheckElimination: elimination)));

        var printed = Print(assets, result);
        foreach (var name in new[]
                 {
                     "EntryPoint::IsPrime", "EntryPoint::IsPrimeDivRem", "EntryPoint::ByteSums", "EntryPoint::Masked",
                     "EntryPoint::Handler", "EntryPoint::Scaled",
                 })
        {
            // Every check these functions have, the operands rule out.
            Assert.Equal(elimination == ManagedIntegerCheckElimination.Disabled,
                Function(printed, name).Contains("call $managed.throw", StringComparison.Ordinal));
        }
        foreach (var (input, expected) in Cases)
        {
            Assert.Equal(expected,
                ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, input, target, result.StaticDataEnd));
        }
    }

    private static string Print(TestAssets assets, CompilationResult result)
    {
        var module = Path.Combine(assets.Directory, "application.wasm");
        File.WriteAllBytes(module, result.ApplicationModule);
        using var services = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
        var printed = services.GetRequiredService<IWasmTools>().Run("print", module);
        Assert.True(printed.ExitCode == 0, printed.StandardError);
        return printed.StandardOutput;
    }

    private static string Function(string printed, string name)
    {
        var function = Regex.Match(printed,
            $@"\(func \${Regex.Escape(name)}\b.*?(?=\n  \(func |\n  \(type |\z)", RegexOptions.Singleline);
        Assert.True(function.Success, $"{name} was not emitted.");
        return function.Value;
    }
}
