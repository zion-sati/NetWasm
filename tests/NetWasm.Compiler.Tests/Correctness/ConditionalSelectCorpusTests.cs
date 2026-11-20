// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tests.Correctness;

// Branches whose arms only compute numbers become selects, and && and || conditions merge into one branch: every
// comparison kind on every number kind, NaN among the floating-point operands, truth values other than 1, triangles,
// diamonds that store or return, and nested conditionals all answer as desktop .NET does.
public sealed class ConditionalSelectCorpusTests(CorrectnessTestRunner runner)
{
    [Theory]
    [InlineData(FinalWasmOptimization.O1)]
    [InlineData(FinalWasmOptimization.O2)]
    [InlineData(FinalWasmOptimization.O3)]
    [InlineData(FinalWasmOptimization.Oz)]
    public void SelectedConditionalsMatchDesktopDotNet(FinalWasmOptimization optimization) =>
        runner.Run(new(
            "ConditionalSelectCorpus",
            "NetWasm.Correctness.ConditionalSelects",
            Source,
            [.. Enumerable.Range(-12, 25)])
        {
            Optimization = optimization,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
            ReportAllMismatches = true,
        });

    private const string Source = """
            using System;
            using System.Runtime.CompilerServices;

            namespace NetWasm.Correctness.ConditionalSelects;

            public static class EntryPoint
            {
                private static int _trace;

                public static int Run(int input)
                {
                    var hash = 17;
                    void Mix(long value) => hash = unchecked(hash * 31 + (int)value ^ (int)(value >> 32));
                    void MixDouble(double value) => Mix(BitConverter.DoubleToInt64Bits(value));

                    int a = input, b = 3 - input, c = input * input - 7;
                    uint ua = unchecked((uint)a), ub = unchecked((uint)b);
                    long la = a * 1_000_000_007L, lb = b * 999_999_937L;
                    nint na = a, nb = b;
                    double nan = double.NaN;
                    double da = input % 4 == 0 ? nan : input * 0.75, db = input % 3 == 0 ? nan : 2.5 - input;
                    float fa = (float)da, fb = (float)db;

                    // Every integer comparison, signed and unsigned, each way round.
                    Mix(a >= b ? a - b : b - a);
                    Mix(a > b ? a : b);
                    Mix(a <= b ? a << 2 : b >> 1);
                    Mix(a < b ? ~a : -b);
                    Mix(a == b ? 1 : 2);
                    Mix(a != b ? a ^ b : a & b);
                    Mix(ua >= ub ? ua - ub : ub - ua);
                    Mix(ua > ub ? (int)ua : (int)ub);
                    Mix(ua <= ub ? 5 : 6);
                    Mix(ua < ub ? ua >> 3 : ub << 3);
                    Mix(la >= lb ? la - lb : lb - la);
                    Mix(la < lb ? la : lb);
                    Mix(na >= nb ? na - nb : nb - na);
                    Mix(na < nb ? na * 3 : nb + 1);
                    Mix(a > 0 ? (long)a * 3 : (long)b - 1);
                    MixDouble(a > 0 ? (float)a : (float)b);

                    // Floating-point comparisons with and without NaN, in the forms Roslyn branches on.
                    MixDouble(da >= db ? da : db);
                    MixDouble(da > db ? da - db : db - da);
                    MixDouble(da <= db ? 1.5 : -1.5);
                    MixDouble(da < db ? da * 2 : db * 2);
                    MixDouble(da == db ? 3 : 4);
                    MixDouble(da != db ? 5 : 6);
                    MixDouble(!(da >= db) ? 7 : 8);
                    MixDouble(!(da < db) ? 9 : 10);
                    MixDouble(!(da > db) ? 11 : 12);
                    MixDouble(!(da <= db) ? 13 : 14);
                    MixDouble(fa >= fb ? fa : fb);
                    MixDouble(!(fa < fb) ? fa - fb : fb - fa);

                    // Triangles and diamonds that store, as abs and clamps are written.
                    var x = a;
                    if (x < 0) x = -x;
                    Mix(x);
                    var y = b;
                    if (y > 4) y = 4; else y = y - 1;
                    Mix(y);
                    var z = c;
                    if (z >= 10) z = 10;
                    if (z <= -10) z = -10;
                    Mix(z);
                    var w = la;
                    if (w < 0) w = ~w;
                    Mix(w);

                    // Truth values other than 1 in && and ||.
                    int p = input & 6, q = input & 3;
                    Mix(p != 0 && q != 0 ? 1 : 0);
                    Mix(p != 0 || q != 0 ? 1 : 0);
                    Mix(p != 0 && q == 0 ? 2 : 3);
                    Mix(p == 0 || q != 0 ? 4 : 5);
                    Mix(a <= b && a <= c ? a : b <= c ? b : c);
                    Mix(a > b || a > c ? 6 : 7);
                    Mix(la != 0 && lb != 0 ? 8 : 9);

                    // Returns, nesting and the predictor PNG's Paeth filter is built on.
                    Mix(Max(a, b));
                    Mix(Clamp(c, -5, 5));
                    Mix(Paeth(a & 255, b & 255, c & 255));
                    Mix(PaethNegating(a & 255, b & 255, c & 255));
                    Mix(Sign(la));
                    Mix(Pick(input, a, b, c));
                    _trace = hash;
                    return hash;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int Max(int a, int b)
                {
                    if (a > b) return a;
                    return b;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int Clamp(int value, int low, int high) =>
                    value < low ? low : value > high ? high : value;

                private static int Paeth(int a, int b, int c)
                {
                    int num = a + b - c;
                    int num2 = ((num >= a) ? (num - a) : (a - num));
                    int num3 = ((num >= b) ? (num - b) : (b - num));
                    int num4 = ((num >= c) ? (num - c) : (c - num));
                    if (num2 <= num3 && num2 <= num4) return a;
                    if (num3 > num4) return c;
                    return b;
                }

                private static int PaethNegating(int a, int b, int c)
                {
                    int p = a + b - c;
                    int pa = p - a, pb = p - b, pc = p - c;
                    pa = pa >= 0 ? pa : -pa;
                    pb = pb >= 0 ? pb : -pb;
                    pc = pc >= 0 ? pc : -pc;
                    if (pa <= pb && pa <= pc) return a;
                    if (pb > pc) return c;
                    return b;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static long Sign(long value) => value > 0 ? 1 : value < 0 ? -1 : 0;

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int Pick(int input, int a, int b, int c) =>
                    (input & 1) != 0 ? ((input & 2) != 0 ? a : b) : ((input & 4) != 0 ? c : a + b);

                public static int Trace() => _trace;
            }
            """;
}
