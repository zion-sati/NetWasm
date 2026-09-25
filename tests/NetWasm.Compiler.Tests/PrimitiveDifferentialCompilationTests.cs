using System.Globalization;
using System.Text;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class PrimitiveDifferentialCompilationTests
{
    [Fact]
    public void CompilerMatchesDesktopDotNetForSeededDoubleRoundTrips()
    {
        var random = new Random(0x4e657457);
        var cases = new List<(long Bits, string Text)>();
        while (cases.Count < 128)
        {
            var bits = random.NextInt64(long.MinValue, long.MaxValue);
            var value = BitConverter.Int64BitsToDouble(bits);
            if (double.IsFinite(value))
            {
                cases.Add((
                    bits,
                    value.ToString("R", CultureInfo.InvariantCulture)));
            }
        }

        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "FloatingDifferentialFixture",
            BuildFloatingFixture(cases));
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "FloatingDifferentialFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(0, ExecuteWithNode(
            compilation.ApplicationModule,
            assets.Directory,
            0));
    }

    [Fact]
    public void CompilerMatchesDesktopDotNetForSeededSingleRoundTrips()
    {
        var random = new Random(0x53696e67);
        var cases = new List<(int Bits, string Text)>();
        while (cases.Count < 256)
        {
            var bits = (int)random.NextInt64(int.MinValue, (long)int.MaxValue + 1);
            var value = BitConverter.Int32BitsToSingle(bits);
            if (float.IsFinite(value))
            {
                cases.Add((
                    bits,
                    value.ToString("R", CultureInfo.InvariantCulture)));
            }
        }

        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "SingleDifferentialFixture",
            BuildSingleFixture(cases));
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "SingleDifferentialFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(0, ExecuteWithNode(
            compilation.ApplicationModule,
            assets.Directory,
            0));
    }

    private static string BuildFloatingFixture(
        IReadOnlyList<(long Bits, string Text)> cases)
    {
        var bits = string.Join(
            ",\n",
            cases.Select(item => $"unchecked((long)0x{unchecked((ulong)item.Bits):x16}UL)"));
        var text = string.Join(
            ",\n",
            cases.Select(item => $"\"{Escape(item.Text)}\""));
        return $$"""
            using System;

            namespace FloatingDifferentialFixture;

            public static class EntryPoint
            {
                private static readonly long[] Bits =
                [
                    {{bits}}
                ];

                private static readonly string[] Text =
                [
                    {{text}}
                ];

                public static int Run(int input)
                {
                    for (var index = 0; index < Bits.Length; index++)
                    {
                        var value = BitConverter.Int64BitsToDouble(Bits[index]);
                        if (value.ToString("R") != Text[index]) return index + 1;
                        if (BitConverter.DoubleToInt64Bits(double.Parse(Text[index])) != Bits[index])
                            return index + 1001;
                    }
                    return input;
                }
            }
            """;
    }

    private static string BuildSingleFixture(
        IReadOnlyList<(int Bits, string Text)> cases)
    {
        var bits = string.Join(
            ",\n",
            cases.Select(item => $"unchecked((int)0x{unchecked((uint)item.Bits):x8}U)"));
        var text = string.Join(
            ",\n",
            cases.Select(item => $"\"{Escape(item.Text)}\""));
        return $$"""
            using System;

            namespace SingleDifferentialFixture;

            public static class EntryPoint
            {
                private static readonly int[] Bits =
                [
                    {{bits}}
                ];

                private static readonly string[] Text =
                [
                    {{text}}
                ];

                public static int Run(int input)
                {
                    for (var index = 0; index < Bits.Length; index++)
                    {
                        var value = BitConverter.Int32BitsToSingle(Bits[index]);
                        if (value.ToString("R") != Text[index]) return index + 1;
                        if (BitConverter.SingleToInt32Bits(float.Parse(Text[index])) != Bits[index])
                            return index + 1001;
                    }
                    return input;
                }
            }
            """;
    }

    private static string Escape(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal);
}
