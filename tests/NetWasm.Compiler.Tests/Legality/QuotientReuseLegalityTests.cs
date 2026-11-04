using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;

namespace NetWasm.Compiler.Tests.Legality;

// A remainder takes an earlier division's quotient only while both operands
// still hold the values the division divided, and only for the same
// division. Each method breaks one condition that must stop it: the dividend
// and the divisor stored between them, a division on one arm only, a
// division in a loop that may not run, the dividend changed through its
// address, a finally and a catch storing an operand on the way to the
// remainder, an unsigned remainder after a signed division and a signed one
// after an unsigned division, the remainder first, another divisor, swapped
// operands, a different constant, doubles, a division that throws with the
// remainder in its handler, a filter's remainder, the dividend stored in the
// loop body before its remainder, a division by a value a branch chose, a
// dividend a branch chose, and a local changed through its address and one a
// finally changes. The rest are remainders that do take
// the quotient: negative and MinValue dividends, longs, native ints, unsigned
// ints, small integers, constant divisors and the trial division of #166.
public sealed class QuotientReuseLegalityTests
{
    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;

        public static class EntryPoint
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Bump(ref int value) => value += 3;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void ThrowFrom(int value)
            {
                if (value >= 0) throw new InvalidOperationException();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static bool Matches(int value, int input) => value == input % 3;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int DividendStored(int a, int b)
            {
                var q = a / b;
                a += 5;
                return q * 1000 + a % b;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int DivisorStored(int a, int b)
            {
                var q = a / b;
                b += 2;
                return q * 1000 + a % b;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int OneArm(int a, int b, int input)
            {
                var q = 0;
                if ((input & 1) == 0) q = a / b;
                return q * 1000 + a % b;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ZeroTrip(int a, int b, int count)
            {
                var q = 0;
                for (var i = 0; i < count; i++) q += a / b;
                return q * 1000 + a % b;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int AddressTaken(int a, int b)
            {
                var q = a / b;
                Bump(ref a);
                return q * 1000 + a % b;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int FinallyStores(int a, int b)
            {
                int q;
                try
                {
                    q = a / b;
                }
                finally
                {
                    a += 7;
                }
                return q * 1000 + a % b;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int CatchStores(int a, int b, int input)
            {
                var q = 0;
                try
                {
                    q = a / b;
                    ThrowFrom(input - 2);
                }
                catch (InvalidOperationException)
                {
                    b += 1;
                }
                return q * 1000 + a % b;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int SignedThenUnsigned(int a, int b)
            {
                var q = a / b;
                return q * 1000 + (int)((uint)a % (uint)b);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int UnsignedThenSigned(int a, int b)
            {
                var q = (int)((uint)a / (uint)b);
                return q * 1000 + a % b;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int RemainderFirst(int a, int b)
            {
                var r = a % b;
                return r * 1000 + a / b;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int OtherDivisor(int a, int b, int c)
            {
                var q = a / b;
                return q * 1000 + a % c;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Swapped(int a, int b)
            {
                var q = a / b;
                return q * 1000 + b % a;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int OtherConstant(int a)
            {
                var q = a / 10;
                return q * 1000 + a % 100;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Doubles(double a, double b)
            {
                var q = a / b;
                return (int)(q * 1000 + a % b * 10);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ThrowingDivision(int a, int b)
            {
                try
                {
                    return a / b;
                }
                catch (DivideByZeroException)
                {
                    try
                    {
                        return a % b;
                    }
                    catch (DivideByZeroException)
                    {
                        return -1;
                    }
                }
                catch (OverflowException)
                {
                    try
                    {
                        return a % b;
                    }
                    catch (OverflowException)
                    {
                        return -2;
                    }
                }
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Filter(int a, int b, int input)
            {
                var q = 0;
                try
                {
                    q = a / b;
                    ThrowFrom(input - 3);
                    return q;
                }
                catch (InvalidOperationException) when (Matches(a % b, input))
                {
                    return q * 7;
                }
                catch (InvalidOperationException)
                {
                    return q * 11;
                }
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int LoopStoreBeforeRemainder(int n)
            {
                var count = 0;
                for (var d = 3; d <= n / d; d += 2)
                {
                    n -= 1;
                    if (n % d == 0) count++;
                }
                return count * 1000 + n;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int BranchyDivisor(int a, int b, int d, bool first)
            {
                var q = a / (first ? b : d);
                return q * 1000 + a % d;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int BranchyDividend(int a, int c, int d, bool first)
            {
                var q = (first ? a : c) / d;
                return q * 1000 + a % d;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int LocalAddressTaken(int a, int b)
            {
                var e = a;
                var q = e / b;
                Bump(ref e);
                return q * 1000 + e % b;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int LocalFinallyStores(int a, int b)
            {
                var e = a;
                int q;
                try
                {
                    q = e / b;
                }
                finally
                {
                    e += 7;
                }
                return q * 1000 + e % b;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Pairs(int a, int b)
            {
                var q = a / b;
                return q * 1000 + a % b;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Widths(long a, long b, nint c, nint d, uint e, uint f, ulong g, ulong h)
            {
                var q1 = a / b;
                var q2 = c / d;
                var q3 = e / f;
                var q4 = g / h;
                return (int)(q1 * 31 + a % b) ^ (int)(q2 * 17 + c % d) ^ (int)(q3 * 13 + e % f) ^ (int)(q4 * 7 + g % h);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Small(short a, sbyte b)
            {
                var q = a / b;
                return q * 1000 + a % b;
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
            private static int Primes(int limit)
            {
                var count = 0;
                for (var n = 0; n < limit; n++) if (IsPrime(n)) count++;
                return count;
            }

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
                // At 1 the division is MinValue / -1, at 2 a division by zero. The
                // native ints stay within 32 bits, which wasm32's are.
                var a = input == 1 ? int.MinValue : input * 37 - 50;
                var b = input - 2;
                var result = Guard(() => DividendStored(a, b));
                result = result * 31 + Guard(() => DivisorStored(a, b));
                result = result * 31 + Guard(() => OneArm(a, b, input));
                result = result * 31 + Guard(() => ZeroTrip(a, b, input & 1));
                result = result * 31 + Guard(() => AddressTaken(a, b));
                result = result * 31 + Guard(() => FinallyStores(a, b));
                result = result * 31 + Guard(() => CatchStores(a, b, input));
                result = result * 31 + Guard(() => SignedThenUnsigned(a, b));
                result = result * 31 + Guard(() => UnsignedThenSigned(a, b));
                result = result * 31 + Guard(() => RemainderFirst(a, b));
                result = result * 31 + Guard(() => OtherDivisor(a, b, input + 5));
                result = result * 31 + Guard(() => Swapped(a, b));
                result = result * 31 + Guard(() => OtherConstant(a));
                result = result * 31 + Guard(() => Doubles(a + 0.5, b - 0.3));
                result = result * 31 + ThrowingDivision(a, b);
                result = result * 31 + Guard(() => Filter(a, b, input));
                result = result * 31 + LoopStoreBeforeRemainder(1000 + input * 97);
                result = result * 31 + Guard(() => BranchyDivisor(a, b, input + 7, input < 3));
                result = result * 31 + Guard(() => BranchyDividend(a, a + 11, input + 7, input < 3));
                result = result * 31 + Guard(() => LocalAddressTaken(a, b));
                result = result * 31 + Guard(() => LocalFinallyStores(a, b));
                result = result * 31 + Guard(() => Pairs(a, b));
                result = result * 31 + Guard(() => Pairs(a, 7));
                result = result * 31 + Guard(() => Widths(a * 3L, b - 3L, a >> 1, b, (uint)a, (uint)b, (ulong)(uint)a, (ulong)(uint)b));
                result = result * 31 + Guard(() => Small((short)a, (sbyte)b));
                result = result * 31 + Digits(a);
                result = result * 31 + Primes(200 + input * 10);
                return result;
            }
        }

        """;

    // As desktop .NET 11 computes them.
    private static readonly (int Input, int Expected)[] Cases =
        [(0, -698437736), (1, 1138431551), (2, 608042932), (3, 1514599087), (6, -1631105610)];

    [Theory]
    [InlineData(ManagedQuotientReuse.Auto)]
    [InlineData(ManagedQuotientReuse.Enabled)]
    [InlineData(ManagedQuotientReuse.Disabled)]
    public void ReusesAQuotientOnlyForTheValuesItsDivisionDivided(ManagedQuotientReuse reuse) =>
        LegalityCases.Run($"QuotientReuseLegality{reuse}", Source, Cases, FinalWasmOptimization.O3,
            new WasmOptimizationOverrides(QuotientReuse: reuse));
}
