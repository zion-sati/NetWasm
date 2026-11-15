// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Globalization;
using System.Text;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class IntegerDefaultFormattingCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void DefaultIntegerConversionsMatchDesktopAcrossWidths(bool optimize, WasmTarget target) =>
        VerifyConversions(optimize, target, straightLine: false);

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void StraightLineIntegerBoundaryChecksDoNotExhaustTheCompilerStack(bool optimize, WasmTarget target) =>
        VerifyConversions(optimize, target, straightLine: true);

    private static void VerifyConversions(bool optimize, WasmTarget target, bool straightLine)
    {
        using var assets = TestAssets.Create();
        var checks = new StringBuilder();
        var count = 0;
        Add("sbyte", [sbyte.MinValue, -1, 0, 1, sbyte.MaxValue]);
        Add("byte", [0, 1, 9, 10, 99, 100, byte.MaxValue]);
        Add("short", [short.MinValue, -1, 0, 1, short.MaxValue]);
        Add("ushort", [0, 1, ushort.MaxValue]);
        Add("int", [int.MinValue, int.MaxValue, .. Boundaries(9)]);
        Add("uint", [uint.MaxValue, .. Boundaries(9).Where(value => value >= 0)]);
        Add("long", [long.MinValue, long.MaxValue, .. Boundaries(18)]);
        Add("ulong", [ulong.MaxValue, .. Boundaries(19).Where(value => value >= 0)]);
        Add("nint", [int.MinValue, 0, int.MaxValue]);
        Add("nuint", [0, uint.MaxValue]);
        Add("Int128", [Int128.MinValue, Int128.MaxValue, .. Boundaries(38)]);
        Add("UInt128", [.. Boundaries(38).Where(value => value >= 0)]);
        var unsignedMaximum = UInt128.MaxValue.ToString(CultureInfo.InvariantCulture);
        checks.AppendLine(CultureInfo.InvariantCulture, $$"""if (UInt128.MaxValue.ToString() != "{{unsignedMaximum}}") return -1;""");
        count++;
        var source = $$"""
            using System;
            using System.Globalization;
            namespace IntegerDefaultFormattingFixture;
            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    if (short.Log2(0) != 0 || ushort.Log2(0) != 0 || char.Log2('\0') != 0) return -4;
                    for (var bit = 0; bit < 16; bit++)
                    {
                        var power = (ushort)(1 << bit);
                        if (ushort.Log2(power) != bit || char.Log2((char)power) != bit) return -5;
                        if (bit < 15 && short.Log2((short)power) != bit) return -6;
                    }
                    try { _ = short.Log2(-1); return -7; }
                    catch (ArgumentOutOfRangeException) { }
                    {{checks}}
                    return {{count}};
                }
            }
            """;
        var assembly = optimize
            ? assets.CompileOptimizedSource("IntegerDefaultFormattingFixture", source)
            : assets.CompileSource("IntegerDefaultFormattingFixture", source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "IntegerDefaultFormattingFixture.EntryPoint", "Run", [], Target: target));

        Assert.Equal(count, ExecuteWithStandardWasiNode(
            result.ApplicationModule, assets.Directory, 0, target, result.StaticDataEnd));

        void Add(string type, IEnumerable<Int128> values)
        {
            var failureBase = count + 10;
            var literals = new List<string>();
            var expectations = new List<string>();
            foreach (var value in values.Distinct())
            {
                var expected = value.ToString(CultureInfo.InvariantCulture);
                var bits = unchecked((UInt128)value);
                var expression = string.Create(CultureInfo.InvariantCulture, $"unchecked(({type})new UInt128({(ulong)(bits >> 64)}UL, {(ulong)bits}UL))");
                literals.Add(expression);
                expectations.Add('"' + expected + '"');
                count += 2;
                if (straightLine)
                {
                    checks.AppendLine(CultureInfo.InvariantCulture, $$"""
                        {
                            {{type}} value = {{expression}};
                            if (value.ToString() != "{{expected}}") return -{{count}};
                            if (value.ToString(CultureInfo.InvariantCulture) != "{{expected}}") return -{{count + 1}};
                        }
                        """);
                }
            }
            if (straightLine)
                return;
            checks.AppendLine(CultureInfo.InvariantCulture, $$"""
                {
                    {{type}}[] values = [{{string.Join(",", literals)}}];
                    string[] expected = [{{string.Join(",", expectations)}}];
                    for (var index = 0; index < values.Length; index++)
                    {
                        if (values[index].ToString() != expected[index]) return -({{failureBase}} + index * 2);
                        if (values[index].ToString(CultureInfo.InvariantCulture) != expected[index]) return -({{failureBase}} + index * 2 + 1);
                    }
                }
                """);
        }
    }

    private static IEnumerable<Int128> Boundaries(int maximumPower)
    {
        yield return 0;
        yield return 299;
        yield return 300;
        yield return 301;
        var power = Int128.One;
        for (var exponent = 0; exponent <= maximumPower; exponent++)
        {
            yield return power - 1;
            yield return power;
            yield return power + 1;
            yield return -power - 1;
            yield return -power;
            yield return -power + 1;
            if (exponent != maximumPower) power *= 10;
        }
    }
}
