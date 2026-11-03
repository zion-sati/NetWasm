using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;

namespace NetWasm.Compiler.Tests.Legality;

// A walk over n elements, `do { ...; p = ref Unsafe.Add(ref p, 1); n--; } while
// (n != 0)`, runs four elements a group, or a vector of them, only when its
// body is straight-line code that moves p on once, counts n down once and
// cannot throw. Each method breaks one condition: a branch in the body, a read
// of the count, a local's address, an argument stored and addressed, two
// steps of the reference, two decrements, a division by an element that is
// zero and a checked conversion that overflows, each caught around the walk,
// the reference passed on, and a body longer than the unroller takes. The
// rest run shifts, bitwise operations and a narrowing, widening and floating
// conversions, a fold of floats, and countdowns that walk no reference.
public sealed class ReferenceWalkLegalityTests
{
    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;

        public static class EntryPoint
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Twice(ref int value) => value *= 2;

            // The body branches.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Branching(int[] values, int count)
            {
                var sum = 0;
                ref var p = ref values[0];
                var n = count;
                do { if (p > 25) sum += p; else sum -= p; p = ref Unsafe.Add(ref p, 1); n--; } while (n != 0);
                return sum;
            }

            // The body reads the count.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int CountRead(int[] values, int count)
            {
                var sum = 0;
                ref var p = ref values[0];
                var n = count;
                do { sum += p * n; p = ref Unsafe.Add(ref p, 1); n--; } while (n != 0);
                return sum;
            }

            // The body takes a local's address.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int LocalAddress(int[] values, int count)
            {
                int sum = 0, other = 1;
                ref var p = ref values[0];
                var n = count;
                do { sum += p | 1; Twice(ref other); p = ref Unsafe.Add(ref p, 1); n--; } while (n != 0);
                return sum ^ other;
            }

            // The body stores and addresses an argument.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ArgumentWritten(int[] values, int count, int scale)
            {
                var sum = 0;
                ref var p = ref values[0];
                var n = count;
                do { sum += p * scale; scale = sum & 7; Twice(ref scale); p = ref Unsafe.Add(ref p, 1); n--; } while (n != 0);
                return sum;
            }

            // The body moves the reference on twice.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int TwoSteps(int[] values, int count)
            {
                var sum = 0;
                ref var p = ref values[0];
                var n = count;
                do { sum += p; p = ref Unsafe.Add(ref p, 1); sum ^= p << 1; p = ref Unsafe.Add(ref p, 1); n--; } while (n != 0);
                return sum;
            }

            // The body counts down twice before the reference moves on last.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int TwoDecrements(int[] values, int count)
            {
                var sum = 0;
                ref var p = ref values[0];
                var n = count * 2;
                do { n--; sum += p | 2; n--; p = ref Unsafe.Add(ref p, 1); } while (n != 0);
                return sum;
            }

            // The body divides by an element that may be zero.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Division(int[] values, int count)
            {
                var sum = 0;
                ref var p = ref values[0];
                var n = count;
                try
                {
                    do { sum += 1000 / (p - 9); p = ref Unsafe.Add(ref p, 1); n--; } while (n != 0);
                }
                catch (DivideByZeroException) { return -sum; }
                return sum;
            }

            // The body converts with an overflow check.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int CheckedConversion(int[] values, int count)
            {
                var sum = 0;
                ref var p = ref values[0];
                var n = count;
                try
                {
                    do { sum += checked((byte)(p * 6)); p = ref Unsafe.Add(ref p, 1); n--; } while (n != 0);
                }
                catch (OverflowException) { return -sum; }
                return sum;
            }

            // The body passes the reference on.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ReferencePassed(int[] values, int count)
            {
                ref var p = ref values[0];
                var n = count;
                do { Twice(ref p); p = ref Unsafe.Add(ref p, 1); n--; } while (n != 0);
                var hash = 17;
                foreach (var value in values) hash = unchecked(hash * 31 + value);
                return hash;
            }

            // A body longer than the unroller takes.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int LongBody(int[] values, int count)
            {
                int a = 0, b = 1, c = 2, d = 3;
                ref var p = ref values[0];
                var n = count;
                do
                {
                    var v = p;
                    a += v * 3 + b; b ^= v << 2 | c; c -= v >> 1 ^ d; d += (v & 5) * a - b;
                    a ^= c + d; b += a - v; c ^= b * 7; d -= c + a;
                    p = ref Unsafe.Add(ref p, 1);
                    n--;
                } while (n != 0);
                return a ^ b ^ c ^ d;
            }

            // Shifts, negation and the unsigned shift, element by element.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Shifts(int[] values, int count)
            {
                var narrow = 0;
                ref var p = ref values[0];
                var n = count;
                do { var v = p; narrow += (v << 3) - (v >> 1) + (int)((uint)v >> 2) + -v; p = ref Unsafe.Add(ref p, 1); n--; } while (n != 0);
                return narrow;
            }

            // Bitwise operations, a product and a narrowing.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Bits(int[] values, int count)
            {
                var narrow = 0;
                ref var p = ref values[0];
                var n = count;
                do { var v = p; narrow ^= ~v + (v & 6) * (v | 1) + (v ^ 9) + (byte)(v * 7); p = ref Unsafe.Add(ref p, 1); n--; } while (n != 0);
                return narrow;
            }

            // Widening and native conversions.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long Widening(int[] values, int count, long scale)
            {
                long wide = 0;
                ref var p = ref values[0];
                var n = count;
                do { var v = p; wide += (long)(uint)v * scale + (nint)v + (long)(nuint)(uint)v; p = ref Unsafe.Add(ref p, 1); n--; } while (n != 0);
                return wide;
            }

            // Floating conversions and arithmetic.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static double Floating(int[] values, int count)
            {
                var real = 0.0;
                ref var p = ref values[0];
                var n = count;
                do { var v = p; real += (float)v * 0.25f + (double)(uint)v * 0.5 + (ulong)(uint)v; p = ref Unsafe.Add(ref p, 1); n--; } while (n != 0);
                return real;
            }

            // Countdowns that walk no reference, counting down first and last.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int CountdownFirst(int[] values, int count)
            {
                var sum = 0;
                var n = count;
                do { n--; sum += values[n] * 3; } while (n != 0);
                return sum;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int CountdownLast(int[] values, int count)
            {
                var sum = 0;
                var n = count;
                do { sum += values[n - 1] ^ n; n--; } while (n != 0);
                return sum;
            }

            // A walk that folds floats.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static float WalkFloats(float[] values, int count)
            {
                var sum = 0f;
                ref var p = ref values[0];
                var n = count;
                do { sum += p; p = ref Unsafe.Add(ref p, 1); n--; } while (n != 0);
                return sum;
            }

            public static int Run(int input)
            {
                var which = input / 100; input %= 100;
                var values = new int[13 + input];
                for (var i = 0; i < values.Length; i++) values[i] = (i * 37 + input) % 50 + 1;
                var count = values.Length - input % 4;
                return which switch
                {
                    0 => Branching(values, count),
                    1 => CountRead(values, count),
                    2 => LocalAddress(values, count),
                    3 => ArgumentWritten(values, count, input + 3),
                    4 => TwoSteps(values, count / 2),
                    5 => TwoDecrements(values, count),
                    6 => Division(values, count),
                    7 => CheckedConversion(values, count),
                    8 => ReferencePassed(values, count),
                    9 => LongBody(values, count),
                    10 => Shifts(values, count),
                    11 => Bits(values, count),
                    12 => (int)(Widening(values, count, input + 2) % 1000000007),
                    13 => (int)(Floating(values, count) * 4),
                    14 => CountdownFirst(values, count),
                    15 => CountdownLast(values, count),
                    16 => BitConverter.SingleToInt32Bits(WalkFloats(Array.ConvertAll(values, value => value * 0.37f + 1e7f * (value % 3)), count)),
                    _ => -9,
                };
            }
        }

        """;

    // As desktop .NET 11 computes them.
    private static readonly (int Input, int Expected)[] Cases = [(0, 149), (1, 200), (2, 149), (3, 200), (6, 199), (100, 2309), (101, 2400), (102, 2041), (103, 2132), (106, 3513), (200, 8547), (201, 8561), (202, 8523), (203, 8537), (206, 131481), (300, 3535), (301, 8), (302, 2343), (303, 24), (306, 3779), (400, 354), (401, 352), (402, 68), (403, 38), (406, 88), (500, 365), (501, 372), (502, 337), (503, 352), (506, 419), (600, 613), (601, -963), (602, 1584), (603, 898), (606, 106), (700, -456), (701, -480), (702, -960), (703, -1008), (706, -42), (800, -1017256159), (801, -797004066), (802, -237510732), (803, -400665357), (806, 2042843620), (900, -900801011), (901, -89340522), (902, -1431677156), (903, 210720459), (906, 1687363302), (1000, 2355), (1001, 2441), (1002, 2192), (1003, 2280), (1006, 2705), (1100, 41), (1101, 128), (1102, 321), (1103, 40), (1106, 69), (1200, 1396), (1201, 1810), (1202, 1950), (1203, 2366), (1206, 4010), (1300, 2443), (1301, 2534), (1302, 2275), (1303, 2366), (1306, 2807), (1400, 1047), (1401, 1086), (1402, 975), (1403, 1014), (1406, 1203), (1500, 318), (1501, 365), (1502, 318), (1503, 357), (1506, 392), (1600, 1291318431), (1601, 1292207033), (1602, 1291318432), (1603, 1292207032), (1606, 1294082033)];

    public static TheoryData<ManagedLoopUnrolling, ManagedLoopVectorization> Settings() => new()
    {
        { ManagedLoopUnrolling.Auto, ManagedLoopVectorization.Auto },
        { ManagedLoopUnrolling.Disabled, ManagedLoopVectorization.Auto },
        { ManagedLoopUnrolling.Auto, ManagedLoopVectorization.Disabled },
    };

    [Theory]
    [MemberData(nameof(Settings))]
    public void WalksRunInGroupsOnlyWhenNoElementCanTellTheDifference(ManagedLoopUnrolling unrolling, ManagedLoopVectorization vectorization) =>
        LegalityCases.Run($"ReferenceWalkLegality{unrolling}{vectorization}", Source, Cases, FinalWasmOptimization.O3,
            new WasmOptimizationOverrides(LoopUnrolling: unrolling, LoopVectorization: vectorization));
}
