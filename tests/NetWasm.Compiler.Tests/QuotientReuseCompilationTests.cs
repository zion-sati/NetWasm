using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// A remainder of values a division just divided is computed from its
// quotient, so a trial division's loop, a digit loop's constant divisor and
// unsigned long and native int pairs emit no remainder: each method keeps
// only its divisions, and gives desktop .NET's results either way (#166). A
// long divided by a constant beyond int's range takes its quotient too.
public sealed class QuotientReuseCompilationTests
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
                for (var divisor = 3; divisor <= value / divisor; divisor += 2)
                {
                    if (value % divisor == 0) return false;
                }
                return true;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Digits(int n)
            {
                var sum = 0;
                while (n != 0)
                {
                    var q = n / 10;
                    sum = sum * 3 + n % 10;
                    n = q;
                }
                return sum;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long Unsigned(ulong a, ulong b)
            {
                var q = a / b;
                return (long)(q * 31 + a % b);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long Large(long a)
            {
                var q = a / 10_000_000_000L;
                return q * 7 + a % 10_000_000_000L;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static nint Native(nint a, nint b)
            {
                var q = a / b;
                return q * 17 + a % b;
            }

            public static int Run(int input)
            {
                var primes = 0;
                for (var n = 0; n < 500 + input; n++) if (IsPrime(n)) primes++;
                return primes * 7 + Digits(input * 12345 - 678) * 3 +
                    (int)Unsigned((ulong)input * 1_000_003 + 17, (ulong)input + 3) +
                    (int)Native(input * 997 - 5000, input + 7) + (int)Large(input * 123_456_789_012L);
            }
        }
        """;

    // As desktop .NET computes them.
    private static readonly (int Input, int Expected)[] Cases = [(0, -11615), (1, -830433522), (7, -75738233), (40, -289517832)];

    [Theory]
    [InlineData(WasmTarget.Wasm32, ManagedQuotientReuse.Auto)]
    [InlineData(WasmTarget.Wasm32, ManagedQuotientReuse.Disabled)]
    [InlineData(WasmTarget.Wasm64, ManagedQuotientReuse.Auto)]
    [InlineData(WasmTarget.Wasm64, ManagedQuotientReuse.Disabled)]
    public void RemaindersOfADivisionsValuesTakeItsQuotient(WasmTarget target, ManagedQuotientReuse reuse)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource($"QuotientReuse_{target}_{reuse}", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Target: target, Optimization: FinalWasmOptimization.O3,
            OptimizationOverrides: new WasmOptimizationOverrides(QuotientReuse: reuse)));

        var printed = Print(assets, result);
        foreach (var name in new[] { "EntryPoint::IsPrime", "EntryPoint::Digits", "EntryPoint::Unsigned", "EntryPoint::Native", "EntryPoint::Large" })
        {
            var function = Function(printed, name);
            Assert.Contains(".div_", function, StringComparison.Ordinal);
            Assert.Equal(reuse == ManagedQuotientReuse.Disabled, function.Contains(".rem_", StringComparison.Ordinal));
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
