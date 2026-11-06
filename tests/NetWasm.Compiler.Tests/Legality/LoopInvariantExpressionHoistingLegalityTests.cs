using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;

namespace NetWasm.Compiler.Tests.Legality;

// A loop's branching computation of values the loop never changes runs once
// ahead of the loop only when it cannot throw and nothing else sees its
// temporaries. Each method breaks one condition: the computation reads a local
// the loop changes, the loop counter, a local whose address escapes, a static
// field, an array element, or a call with effects; it divides integers, uses
// checked arithmetic, or leaves a temporary read after the loop; and a loop
// that runs no iteration must not see a hoisted division by zero. The search
// that SeekUnroll repeats, a floating division behind a branch and a nested
// loop's invariant choice move and still give desktop .NET 11's results.
public sealed class LoopInvariantExpressionHoistingLegalityTests
{
    private const string Source = """
        using System;
        using System.Numerics;
        using System.Runtime.CompilerServices;

        public static class EntryPoint
        {
            private static int s_limit = 3;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Bump(ref int value) => value += 5;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Noisy(int value)
            {
                s_limit++;
                return value + 1;
            }

            // SeekUnroll's FindByte: the lane search moves, the store stays.
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static int FindByte(ref Vector<byte> byteEquals)
            {
                var vector64 = Vector.AsVectorInt64(byteEquals);
                long longValue = 0;
                var i = 0;
                for (; i < Vector<long>.Count; i++)
                {
                    longValue = vector64[i];
                    if (longValue == 0) continue;
                    break;
                }
                var flag = (ulong)(longValue ^ (longValue - 1));
                return i * 8 + (int)((flag * 0x0001020304050608UL) >> 57);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Seek(int index, int iterations)
            {
                var bytes = new byte[Vector<byte>.Count];
                bytes[index % bytes.Length] = 255;
                var vector = new Vector<byte>(bytes);
                var found = -1;
                for (var i = 0; i < iterations; i++) found = FindByte(ref vector);
                return found;
            }

            // A floating division behind a branch cannot throw, so it moves.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static double FloatingChoice(double a, double b, int n)
            {
                var total = 0.0;
                for (var i = 0; i < n; i++) total += a > b ? a / b : b / (a - 1.5);
                return total;
            }

            // An inner loop's choice of an outer value.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Nested(int a, int b, int n)
            {
                var total = 0;
                for (var i = 0; i < n; i++)
                {
                    for (var j = 0; j < n; j++) total += (a > b ? a - b : b - a) + j;
                    a += i;
                }
                return total;
            }

            // The computation reads a local the loop changes.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int WrittenLocal(int a, int b, int n)
            {
                var total = 0;
                for (var i = 0; i < n; i++)
                {
                    total += a > b ? a * 3 : b - a;
                    a += 2;
                }
                return total;
            }

            // The computation reads the loop counter.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Counter(int a, int n)
            {
                var total = 0;
                for (var i = 0; i < n; i++) total += i > a ? i - a : a - i;
                return total;
            }

            // A call in the loop changes a local through its address.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int EscapedLocal(int a, int b, int n)
            {
                var total = 0;
                for (var i = 0; i < n; i++)
                {
                    total += a > b ? a - b : b + a;
                    Bump(ref a);
                }
                return total;
            }

            // A static field the loop changes through a call.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int StaticField(int n)
            {
                var total = 0;
                for (var i = 0; i < n; i++) total += s_limit > 4 ? Noisy(i) : s_limit;
                return total;
            }

            // An array element can be out of range, and the loop may not run.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Element(int[] values, int index, int n)
            {
                var total = 0;
                for (var i = 0; i < n; i++) total += index >= 0 ? values[index] : -1;
                return total;
            }

            // An integer division can throw, and the loop may not run.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int IntegerDivision(int a, int b, int n)
            {
                var total = 0;
                for (var i = 0; i < n; i++) total += a > 0 ? a / b : a % b;
                return total;
            }

            // Checked arithmetic can throw, and the loop may not run.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Checked(int a, int b, int n)
            {
                var total = 0;
                for (var i = 0; i < n; i++) total += a > b ? checked(a * b) : checked(a + b);
                return total;
            }

            // A temporary the computation writes is read after the loop, which may not run.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int TemporaryAfter(int a, int b, int n)
            {
                var chosen = -7;
                var total = 0;
                for (var i = 0; i < n; i++)
                {
                    chosen = a > b ? a : b;
                    total += chosen;
                }
                return total * 100 + chosen;
            }

            // A call with an effect.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int EffectfulCall(int a, int n)
            {
                var total = 0;
                for (var i = 0; i < n; i++) total += a > 0 ? Noisy(a) : a;
                return total;
            }

            private static int Guard(Func<int> run)
            {
                try
                {
                    return run();
                }
                catch (DivideByZeroException)
                {
                    return -11;
                }
                catch (OverflowException)
                {
                    return -12;
                }
                catch (IndexOutOfRangeException)
                {
                    return -13;
                }
            }

            public static int Run(int input)
            {
                s_limit = 3;
                var n = input % 3 == 0 ? 0 : input + 2;
                var result = Seek(input * 7, 1000 + input);
                result = result * 31 + (int)(FloatingChoice(input + 0.25, 1.75, n) * 1000);
                result = result * 31 + Nested(input, 5, n);
                result = result * 31 + WrittenLocal(input, 4, n);
                result = result * 31 + Counter(input, n);
                result = result * 31 + EscapedLocal(input, 3, n);
                result = result * 31 + StaticField(n);
                result = result * 31 + Guard(() => Element(new int[4], input - 2, n));
                result = result * 31 + Guard(() => IntegerDivision(input - 1, input - 3, n));
                result = result * 31 + Guard(() => Checked(input == 4 ? int.MaxValue : input, input + 1, n));
                result = result * 31 + TemporaryAfter(input, 2, n);
                result = result * 31 + EffectfulCall(input - 1, n);
                return result;
            }
        }

        """;

    // As desktop .NET 11 computes them (arm64, where Vector<T> is 16 bytes, as on NetWasm).
    private static readonly (int Input, int Expected)[] Cases =
        [(0, -217), (1, 1527152138), (2, 1374579850), (3, 645413378), (4, 881501529), (5, 313065095), (6, 1290826973)];

    [Theory]
    [InlineData(ManagedLoopInvariantExpressionHoisting.Auto)]
    [InlineData(ManagedLoopInvariantExpressionHoisting.Enabled)]
    [InlineData(ManagedLoopInvariantExpressionHoisting.Disabled)]
    public void HoistsOnlyComputationsThatCannotThrowOfValuesTheLoopKeeps(ManagedLoopInvariantExpressionHoisting hoisting) =>
        LegalityCases.Run($"LoopInvariantExpressionHoistingLegality{hoisting}", Source, Cases, FinalWasmOptimization.O3,
            new WasmOptimizationOverrides(LoopInvariantExpressionHoisting: hoisting));
}
