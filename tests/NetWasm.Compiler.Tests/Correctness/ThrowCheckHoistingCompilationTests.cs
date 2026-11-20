// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

// An arm that stores a value and then checks it, throwing when the check fails, runs as desktop .NET runs it once its
// check moves out of the arm and the arm becomes a select: Math.Abs of every signed integer type over values of both
// signs, MinValue's OverflowException and its message, the same shape written in user code on an argument, on a local,
// for int64 and for double, and a check that holds only on the arm's path. Parameter clamps (if (v < 0) v = 0;) store
// their select to the argument.
public sealed class ThrowCheckHoistingCompilationTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void ArmsThatCheckWhatTheyStoreBehaveAsDesktopDoes() =>
        runner.Run(new("ThrowCheckHoisting", "ThrowCheckHoisting", Source, [0, 1, 7, 40])
        {
            ReportAllMismatches = true,
        });

    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;
        namespace ThrowCheckHoisting;

        public static class EntryPoint
        {
            private static int _trace;
            private static int _hash;

            public static int Trace() => _trace;

            private static void Mix(long value) => _hash = unchecked(_hash * 31 + (int)value + (int)(value >> 32));

            private static void Mix(string text)
            {
                foreach (var c in text) Mix(c);
            }

            // Values of both signs in an order no predictor learns.
            private static int Value(int seed, int i) => unchecked((int)((uint)(seed * 7919 + i) * 2654435761u)) >> (i & 7);

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Clamp(int value, int low, int high)
            {
                if (value < low) value = low;
                if (value > high) value = high;
                return value;
            }

            // The shape on an argument, with an exception of its own.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Halve(int value)
            {
                if (value < 0)
                {
                    value = -value >> 1;
                    if (value > 1_000_000) throw new ArgumentOutOfRangeException(nameof(value));
                }
                return value;
            }

            // On a local, where the check holds only on the arm's path: a value of 7 or more skips the arm and passes
            // on, though 7 or more would fail the check.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Raise(int input)
            {
                var value = input;
                if (value < 7)
                {
                    value = value * 3 + 5;
                    if (value >= 7) throw new InvalidOperationException("raised");
                }
                return value;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long Magnitude(long value)
            {
                if (value < 0)
                {
                    value = -value;
                    if (value < 0) throw new OverflowException("long");
                }
                return value;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static double Positive(double value)
            {
                if (value < 0)
                {
                    value = -value * 0.5;
                    if (value > 1e300) throw new ArithmeticException("double");
                }
                return value;
            }

            private static void Expect(Func<long> run)
            {
                try
                {
                    Mix(run());
                }
                catch (Exception exception)
                {
                    Mix(exception switch
                    {
                        OverflowException => 1,
                        ArgumentOutOfRangeException => 2,
                        InvalidOperationException => 3,
                        ArithmeticException => 4,
                        _ => 5,
                    });
                    Mix(exception.Message);
                }
            }

            public static int Run(int input)
            {
                _hash = 17;
                long sum = 0;
                for (var i = 0; i < 64; i++)
                {
                    var value = Value(input, i);
                    sum += Math.Abs(value);
                    sum += Math.Abs((long)value * 3);
                    sum += Math.Abs((short)value);
                    sum += Math.Abs((sbyte)value);
                    sum += Math.Abs((nint)value);
                    sum += Clamp(value, -1000, 1000);
                    sum += Halve(value % 2_000_001);
                    sum += Magnitude((long)value << 20);
                    sum += (long)Positive(value * 0.25);
                    sum += Raise((value & 15) + 1);
                }
                Mix(sum);
                Expect(() => Math.Abs(int.MinValue + input));
                Expect(() => Math.Abs(long.MinValue + input));
                Expect(() => Math.Abs((short)(short.MinValue + input)));
                Expect(() => Math.Abs((sbyte)(sbyte.MinValue + input)));
                // nint's range is the target's, so the result is compared rather than mixed in.
                Expect(() => Math.Abs(nint.MinValue + input) == nint.MaxValue - input + 1 ? 1 : 0);
                Expect(() => Halve(-4_000_000 - input));
                Expect(() => Raise(input - 3));
                Expect(() => Magnitude(long.MinValue + input));
                Expect(() => (long)Positive(-1e301 - input));
                _trace = _hash;
                return _hash;
            }
        }
        """;
}
