// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// O3 reads plain argument loads in place when it inlines, skips null and
// bounds checks an earlier access already made, and snapshots array metadata
// once for nested loops. Each scenario changes a slot during a call, or makes
// an elided check fail, so a wrong elision shows up as a different result.
public sealed class RepeatedArrayCheckCompilationTests
{
    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;

        public struct Pair
        {
            public int A;
            public int B;
            public Pair(int a, int b) { A = a; B = b; }
            public Pair(Pair other) { A = other.B + 1; B = other.A + 2; }
        }

        public static class EntryPoint
        {
            private static int Combine(int a, int b) => a * 10 + b;

            private static int AddInto(int a, ref int b) { b += a; return a + b; }

            private static int Twice(int a, int b) { a *= 2; return a + b; }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int StoredBetween(int x) => Combine(x, x = 5) * 100 + x;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Exposed(int x) => AddInto(x, ref x) * 100 + x;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int InPlace(int x)
            {
                var value = new Pair(x, x + 3);
                for (var i = 0; i < 4; i++) value = new Pair(value);
                return value.A * 1000 + value.B;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Reassigned(int x, int y) => Twice(x, y) * 100 + x;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int TwoBlocks(int[] values, int index)
            {
                var sum = values[index];
                if (sum > 0) sum += values[index + 1];
                return sum;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int SameIndex(int[] values, int index) => values[index] * values[index] + values[index];

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long Nested(int[] values)
            {
                long sum = 0;
                for (var round = 0; round < 3; round++)
                    for (var i = 0; i < values.Length; i++)
                        for (var j = i; j < values.Length; j += 7)
                            sum += values[j] ^ values[i] + round;
                return sum;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long Swapped(int[] first, int[] second)
            {
                var current = first;
                long sum = 0;
                for (var round = 0; round < 4; round++)
                {
                    for (var i = 0; i < current.Length; i++) sum = sum * 3 + current[i];
                    current = round % 2 == 0 ? second : first;
                }
                return sum;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Proven(int[] values, int count)
            {
                var level = values[0];
                for (var i = 1; i < count; i++)
                {
                    while (level < values[i]) level++;
                }
                return level;
            }

            public static int Run(int input)
            {
                var values = new int[16];
                for (var i = 0; i < values.Length; i++) values[i] = (i * 7 + input) % 11 - 2;
                var other = new int[9];
                for (var i = 0; i < other.Length; i++) other[i] = i * 3 - input;
                try
                {
                    return (input / 10) switch
                    {
                        0 => StoredBetween(input + 1),
                        1 => Exposed(input),
                        2 => InPlace(input),
                        3 => Reassigned(input, input * 3),
                        4 => TwoBlocks(input % 10 == 0 ? null! : values, input % 10 == 1 ? 15 : input % 10),
                        5 => SameIndex(input % 10 == 0 ? null! : values, input % 10 == 1 ? 16 : input % 10),
                        6 => (int)Nested(values),
                        7 => (int)Swapped(values, other),
                        8 => Proven(input % 10 == 0 ? null! : values, input % 10 == 1 ? 17 : 16),
                        _ => -1,
                    };
                }
                catch (NullReferenceException)
                {
                    return -2;
                }
                catch (IndexOutOfRangeException)
                {
                    return -3;
                }
            }
        }
        """;

    // As desktop .NET computes them; -2 is a NullReferenceException and -3 an
    // IndexOutOfRangeException.
    private static readonly (int Input, int Expected)[] Expectations =
    [
        (3, 4505), (13, 3926), (23, 29032), (33, 16533), (40, -2), (41, -3), (43, 10), (50, -2), (51, -3),
        (54, 12), (63, 211), (73, 393904648), (80, -2), (81, -3), (82, 8),
    ];

    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    public void ElidedChecksKeepDesktopResults(WasmTarget target, FinalWasmOptimization preset)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("RepeatedArrayChecks", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Target: target, Optimization: preset));

        foreach (var (input, expected) in Expectations)
        {
            Assert.Equal(expected,
                ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, input, target, result.StaticDataEnd));
        }
    }
}
