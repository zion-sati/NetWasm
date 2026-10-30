using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// BigInteger's explicit integer conversions keep the sign and throw when the
// value is out of range, and truncation keeps the two's complement low bits.
public sealed class BigIntegerConversionCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, 0, -1958482408)]
    [InlineData(WasmTarget.Wasm32, 5, 1731529960)]
    [InlineData(WasmTarget.Wasm64, 0, -1958482408)]
    public void ConvertsToIntegersAsDesktopDoes(WasmTarget target, int input, int expected)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        // As desktop .NET computes it.
        Assert.Equal(expected, executor.Execute(new CompilationScenario(
            $"BigIntegerConversion_{target}_{input}",
            Source,
            "EntryPoint",
            Optimize: true,
            target,
            input,
            [])));
    }

    private const string Source = """
        using System;
        using System.Numerics;

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                BigInteger[] values =
                [
                    -123 - input, 123 + input, -1, 255, 65536, int.MinValue, int.MaxValue,
                    (BigInteger)int.MinValue - 1, (BigInteger)int.MaxValue + 1, uint.MaxValue,
                    long.MinValue, long.MaxValue, (BigInteger)long.MinValue - 1, ulong.MaxValue,
                    (BigInteger)ulong.MaxValue + 1, -BigInteger.Pow(10, 20) - input,
                ];
                var checksum = 0;
                foreach (var value in values)
                {
                    checksum = checksum * 31 + Convert(() => (int)value);
                    checksum = checksum * 31 + Convert(() => (int)(uint)value);
                    checksum = checksum * 31 + Convert(() => (int)((long)value >> 17));
                    checksum = checksum * 31 + Convert(() => (int)((ulong)value >> 19));
                    checksum = checksum * 31 + Convert(() => (short)value + (ushort)value);
                    checksum = checksum * 31 + Convert(() => (byte)value + (sbyte)value + (char)value);
                    checksum = checksum * 31 + (int)(ulong.CreateTruncating(value) >> 13);
                }
                return checksum;
            }

            // An overflow is a result of its own.
            private static int Convert(Func<int> conversion)
            {
                try
                {
                    return conversion();
                }
                catch (OverflowException)
                {
                    return 7919;
                }
            }
        }
        """;
}
