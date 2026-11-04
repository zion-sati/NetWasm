using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;

namespace NetWasm.Compiler.Tests.Legality;

// A division, checked arithmetic or checked conversion drops its check only
// where the ranges of its operands rule the failure out. Each method breaks
// one condition that must keep a check: a loop that counts down through zero,
// a divisor stored after the branch that tested it, one changed through its
// address, one a finally and one a catch store, a product that wraps to zero,
// an unsigned comparison that lets -1 through, a test that MinValue passes, a
// small integer that truncates to zero, a divisor that a stale load tested, a
// handler's divisor, a ternary's divisor, a long divisor that may be zero, a
// native int divisor, a checked sum, an unsigned checked difference and a
// checked conversion that can overflow, a divisor equal to zero tested
// with ==, a divisor decremented after the test that read it, a remainder and
// a quotient of a negative dividend, a division on the side of a test where
// the divisor is zero, a test of a ternary's value either way round, a local
// changed through its address, the side of each signed and unsigned
// comparison that holds zero or -1, a divisor tested equal to zero, one
// tested unequal to zero whose successor may be, both sides of a test against
// another constant, and a range that excludes an inner value. The rest drop their checks and still give desktop's results: a
// positive divisor, the trial division of #166, a dividend tested above
// MinValue, checked sums of bytes and a checked conversion in range.
public sealed class IntegerCheckLegalityTests
{
    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;

        public static class EntryPoint
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Zero(ref int value) => value = 0;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Fail(int value)
            {
                if (value >= 0) throw new InvalidOperationException();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int CountsDownThroughZero(int x, int start)
            {
                var total = 0;
                for (var d = start; d > -3; d--) total += x / d;
                return total;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int StoredAfterTest(int x, int d, int input)
            {
                if (d <= 0) return -1;
                d = input - 2;
                return x / d;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int AddressTaken(int x, int d)
            {
                if (d <= 0) return -1;
                Zero(ref d);
                return x / d;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int FinallyStores(int x, int d, int input)
            {
                if (d <= 0) return -1;
                try
                {
                    Fail(-1);
                }
                finally
                {
                    if (input > 2) d = 0;
                }
                return x / d;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int CatchStores(int x, int d, int input)
            {
                if (d <= 0) return -1;
                try
                {
                    Fail(input - 3);
                }
                catch (InvalidOperationException)
                {
                    d = 0;
                }
                return x / d;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int WrapsToZero(int x, int input)
            {
                var d = int.MinValue + input * 0;
                if (d > 0) return -1;
                return x / (d * 2);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int UnsignedTest(int x, int d)
            {
                if ((uint)d > 5u) return x / d;
                return -1;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int AllValues(int x, int d)
            {
                if (x >= int.MinValue) return x / d;
                return -1;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Truncates(int x, int input)
            {
                var small = (short)(65536 * input + 1);
                if (small < 1) return -1;
                small = (short)(65536 * input);
                return x / small;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int StaleLoad(int x, int input)
            {
                var d = input + 1;
                var tested = d;
                d = input - 2;
                if (tested <= 0) return -1;
                return x / d;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int HandlerDivisor(int x, int input)
            {
                var d = 5;
                try
                {
                    d = input - 2;
                    Fail(input);
                    return 0;
                }
                catch (InvalidOperationException)
                {
                    return x / d;
                }
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Ternary(int x, int d, bool constant) => x / (constant ? 3 : d);

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long LongDivisor(long x, long d)
            {
                if (d > -1) return x / d;
                return -1;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static nint NativeDivisor(nint x, nint d)
            {
                if (d > -1) return x / d;
                return -1;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int CheckedSum(int a, int b)
            {
                if (a < int.MaxValue - 10 || b < 0 || b > 20) return -1;
                return checked(a + b);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int UnsignedDifference(uint a, uint b)
            {
                if (a > 10 || b > 10) return -1;
                return (int)checked(a - b);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int CheckedNarrowing(int value)
            {
                if (value < 0 || value > 300) return -1;
                return checked((byte)value);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int EqualsZero(int x, int d)
            {
                if (d == 0) return x / (d + 1);
                return x / (d - d);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int PostDecrement(int x, int d)
            {
                if (d-- < 1) return -1;
                return x / d;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int RemainderSign(int x, int y) => x / (y % 5 + 4);

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int QuotientSign(int x, int y) => x / (y / 2 + 2);

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ZeroSide(int x, int d)
            {
                if (d < 0) return -1;
                if (d != 0) return 5;
                return x / d;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int MergedOrigin(int x, int d, int input)
            {
                if ((input > 3 ? d : 1) > 0) return x / d;
                return -1;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int MergedOriginReversed(int x, int d, int input)
            {
                if ((input <= 3 ? 1 : d) > 0) return x / d;
                return -1;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int LocalAddressTaken(int x, int d)
            {
                var e = d + 1;
                if (e <= 0) return -1;
                Zero(ref e);
                return x / e;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int LessThan(int x, int d)
            {
                if (d < 5) return x / d;
                return -1;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int NotLessThan(int x, int d)
            {
                if (d >= 5) return -1;
                return x / d;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int AtMost(int x, int d)
            {
                if (d <= 4) return x / d;
                return -1;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int NotAtMost(int x, int d)
            {
                if (d > 4) return -1;
                return x / d;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int GreaterThan(int x, int d)
            {
                if (d > -5) return x / d;
                return -1;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int NotGreaterThan(int x, int d)
            {
                if (d <= -5) return -1;
                return x / d;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int AtLeast(int x, int d)
            {
                if (d >= -4) return x / d;
                return -1;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int NotAtLeast(int x, int d)
            {
                if (d < -4) return -1;
                return x / d;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int UnsignedBelow(int x, int d)
            {
                if ((uint)d < 5u) return x / d;
                return -1;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int UnsignedAtLeast(int x, int d)
            {
                if ((uint)d >= 5u) return x / d;
                return -1;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int UnsignedAtMost(int x, int d)
            {
                if ((uint)d <= 4u) return -1;
                return x / d;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int UnsignedAbove(int x, int d)
            {
                if ((uint)d > 4u) return x / d;
                return -1;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ZeroSideTrue(int x, int d)
            {
                if (d < 0) return -1;
                if (d == 0) return x / d;
                return 5;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int NonZeroSide(int x, int d)
            {
                if (d != 0) return x / (d + 1);
                return -1;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int EqualSide(int x, int d)
            {
                if (d == 3) return 7;
                return x / d;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int NotEqualSide(int x, int d)
            {
                if (d != 3) return x / d;
                return 7;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int InteriorNotEqual(int x, int d)
            {
                if (d < -5 || d > 100) return -1;
                if (d != 0) return x / (d + 1);
                return -2;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Positive(int x, int d)
            {
                if (d < 1) return -1;
                return x / d + x % d;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int AboveMinValue(int x, int d)
            {
                if (x <= int.MinValue || d == 0) return -1;
                return x / d;
            }

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
            private static int Primes(int limit)
            {
                var count = 0;
                for (var n = 0; n < limit; n++) if (IsPrime(n)) count++;
                return count;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ByteSums(byte a, byte b) => checked(a + b) + checked(a * b);

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int InRangeNarrowing(int value) => checked((byte)(value & 0x7F));

            private static int Guard(Func<int> body)
            {
                try
                {
                    return body();
                }
                catch (DivideByZeroException)
                {
                    return -11;
                }
                catch (OverflowException)
                {
                    return -13;
                }
            }

            public static int Run(int input)
            {
                var x = input == 1 ? int.MinValue : input * 37 - 50;
                var d = input - 2;
                var result = Guard(() => CountsDownThroughZero(x, input));
                result = result * 31 + Guard(() => StoredAfterTest(x, input + 1, input));
                result = result * 31 + Guard(() => AddressTaken(x, input + 1));
                result = result * 31 + Guard(() => FinallyStores(x, input + 1, input));
                result = result * 31 + Guard(() => CatchStores(x, input + 1, input));
                result = result * 31 + Guard(() => WrapsToZero(x, input));
                result = result * 31 + Guard(() => UnsignedTest(x, input == 1 ? -1 : d));
                result = result * 31 + Guard(() => AllValues(x, input == 1 ? -1 : d));
                result = result * 31 + Guard(() => Truncates(x, input));
                result = result * 31 + Guard(() => StaleLoad(x, input));
                result = result * 31 + Guard(() => HandlerDivisor(x, input));
                result = result * 31 + Guard(() => Ternary(x, d, input == 3));
                result = result * 31 + Guard(() => (int)LongDivisor(x * 3L, d));
                result = result * 31 + Guard(() => (int)NativeDivisor(x >> 1, d));
                result = result * 31 + Guard(() => CheckedSum(int.MaxValue - 6 + input, input * 2));
                result = result * 31 + Guard(() => UnsignedDifference((uint)input, 3));
                result = result * 31 + Guard(() => CheckedNarrowing(input * 50));
                result = result * 31 + Guard(() => EqualsZero(x, d));
                result = result * 31 + Guard(() => PostDecrement(x, input - 2));
                result = result * 31 + Guard(() => RemainderSign(x, input - 6));
                result = result * 31 + Guard(() => QuotientSign(x, input - 6));
                result = result * 31 + Guard(() => ZeroSide(x, d));
                result = result * 31 + Guard(() => MergedOrigin(x, d, input));
                result = result * 31 + Guard(() => MergedOriginReversed(x, d, input));
                result = result * 31 + Guard(() => LocalAddressTaken(x, d));
                result = result * 31 + Guard(() => LessThan(x, d));
                result = result * 31 + Guard(() => NotLessThan(x, d));
                result = result * 31 + Guard(() => AtMost(x, d));
                result = result * 31 + Guard(() => NotAtMost(x, d));
                result = result * 31 + Guard(() => GreaterThan(x, d));
                result = result * 31 + Guard(() => NotGreaterThan(x, d));
                result = result * 31 + Guard(() => AtLeast(x, d));
                result = result * 31 + Guard(() => NotAtLeast(x, d));
                result = result * 31 + Guard(() => UnsignedBelow(x, d));
                result = result * 31 + Guard(() => UnsignedAtLeast(x, input == 1 ? -1 : d));
                result = result * 31 + Guard(() => UnsignedAtMost(x, input == 1 ? -1 : d));
                result = result * 31 + Guard(() => UnsignedAbove(x, input == 1 ? -1 : d));
                result = result * 31 + Guard(() => ZeroSideTrue(x, d));
                result = result * 31 + Guard(() => NonZeroSide(x, input == 1 ? -1 : d));
                result = result * 31 + Guard(() => EqualSide(x, d));
                result = result * 31 + Guard(() => NotEqualSide(x, d));
                result = result * 31 + Guard(() => InteriorNotEqual(x, input == 1 ? -1 : d));
                result = result * 31 + Guard(() => Positive(x, input + 1));
                result = result * 31 + Guard(() => AboveMinValue(x, input == 1 ? -1 : d));
                result = result * 31 + Primes(200 + input * 10);
                result = result * 31 + Guard(() => ByteSums((byte)(input * 40), (byte)(255 - input)));
                result = result * 31 + Guard(() => InRangeNarrowing(input * 1000));
                return result;
            }
        }

        """;

    // As desktop .NET 11 computes them.
    private static readonly (int Input, int Expected)[] Cases =
        [(0, -1717285303), (1, -328609164), (2, 884278050), (3, 506235283), (6, -373437084)];

    [Theory]
    [InlineData(ManagedIntegerCheckElimination.Auto)]
    [InlineData(ManagedIntegerCheckElimination.Enabled)]
    [InlineData(ManagedIntegerCheckElimination.Disabled)]
    public void DropsAnIntegerCheckOnlyWhereItsOperandsCannotFailIt(ManagedIntegerCheckElimination elimination) =>
        LegalityCases.Run($"IntegerCheckLegality{elimination}", Source, Cases, FinalWasmOptimization.O3,
            new WasmOptimizationOverrides(IntegerCheckElimination: elimination));
}
