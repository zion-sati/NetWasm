using System.Diagnostics;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// BigInteger divides multi-word magnitudes a word of quotient at a time.
public sealed class BigIntegerDivisionCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, 0, -432298841)]
    [InlineData(WasmTarget.Wasm32, 5, -1615741555)]
    [InlineData(WasmTarget.Wasm64, 0, -432298841)]
    public void DividesAsDesktopDoes(WasmTarget target, int input, int expected)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        // As desktop .NET computes it.
        Assert.Equal(expected, executor.Execute(new CompilationScenario(
            $"BigIntegerDivision_{target}_{input}",
            Source,
            "EntryPoint",
            Optimize: true,
            target,
            input,
            [])));
    }

    // A word of quotient at a time, dividing hundred-thousand-digit numbers
    // takes milliseconds; a bit at a time it took over a minute.
    [Fact]
    [Trait("Category", "Performance")]
    public void DividesHundredThousandDigitNumbersQuickly()
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        var watch = Stopwatch.StartNew();
        var result = executor.Execute(new CompilationScenario(
            "BigIntegerLargeDivision",
            LargeSource,
            "EntryPoint",
            Optimize: true,
            WasmTarget.Wasm32,
            0,
            []));

        Assert.Equal(504046027, result);
        Assert.InRange(watch.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(30));
    }

    private const string Source = """
        using System;
        using System.Numerics;

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                // Each needs the step that adds the divisor back after a guessed
                // quotient digit turns out one too high.
                (string Dividend, string Divisor, string Quotient, string Remainder)[] addBacks =
                [
                    ("680564734000333251973724634122186391551", "18446744078004518913", "36893488147419103231", "2147483648"),
                    ("119562371199743134025080826185318924289", "39614081257132168794161659723", "3018178571", "39614081255787194899566528456"),
                    ("730750819005733826033570678944664806809384658588", "39614081284802284915863501887", "18446744069414584319", "11947061155590006379607548635"),
                    ("730750818665451459141456497615273678626885730305", "39614081266355540840069201919", "18446744069414584319", "55340232225423622144"),
                ];
                foreach (var (dividend, divisor, quotient, remainder) in addBacks)
                {
                    if (BigInteger.DivRem(BigInteger.Parse(dividend), BigInteger.Parse(divisor), out var r) != BigInteger.Parse(quotient) ||
                        r != BigInteger.Parse(remainder))
                    {
                        return -1;
                    }
                }

                var checksum = 0;
                var value = BigInteger.Pow(new BigInteger(3), 2000 + input) + 12345;
                var by = BigInteger.Pow(new BigInteger(7), 600) - input;
                for (var step = 0; step < 40; step++)
                {
                    var quotient = BigInteger.DivRem(value, by, out var remainder);
                    if (quotient * by + remainder != value || remainder >= by || remainder.Sign < 0) return -2;
                    checksum = checksum * 31 + (int)(quotient % 1000000007) ^ (int)(remainder % 998244353);
                    value = value * 1000003 + remainder;
                    by += quotient % 977 + 1;
                }
                var negative = BigInteger.DivRem(-value, by, out var negativeRemainder);
                checksum = checksum * 31 + (int)(negative % 1000000007) ^ (int)(negativeRemainder % 998244353);
                return checksum;
            }
        }
        """;

    private const string LargeSource = """
        using System.Numerics;

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var dividend = BigInteger.Pow(new BigInteger(10), 100000) + input;
                var quotient = BigInteger.DivRem(dividend, BigInteger.Pow(new BigInteger(10), 99990) + 7, out var remainder);
                return (int)(quotient % 1000000007) ^ (int)(remainder % 998244353);
            }
        }
        """;
}
