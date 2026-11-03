using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;

namespace NetWasm.Compiler.Tests.Legality;

// A pure Math call repeated on the same arguments takes the first call's
// result only while nothing between can change what the arguments read. Each
// method breaks one condition that must stop it: a local or the argument the
// arguments read stored between, a static field they read stored, a call
// that writes it, a store through a ref that aliases it, a local whose address
// is taken and written through, a repeat in a loop that changes it, other
// arguments, Interlocked on the static, and a handler that stores the local.
// The two calls come before any other call, which would stop the reuse
// itself. A float repeat, and a repeat past another pure call and a store to
// a local the arguments do not read, take the first result. The functions
// round correctly, so every engine gives desktop's bits.
public sealed class PureCallReuseLegalityTests
{
    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;
        using System.Threading;

        public static class EntryPoint
        {
            private static double s_x = 2.25, s_y = 6.25;

            private static int Bits(double value) => (int)(BitConverter.DoubleToInt64Bits(value) % 1000003);

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Bump() => s_x += 1.5;

            // The local an argument reads is stored between the calls.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int LocalStored(double x)
            {
                var a = Math.Sqrt(x * 3);
                x += 1;
                var b = Math.Sqrt(x * 3);
                return Bits(a) ^ Bits(b);
            }

            // The argument itself is stored between the calls.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ArgumentStored(double x, int input)
            {
                var a = Math.Floor(x / 3);
                if (input >= 0) x = x * 7 + input;
                var b = Math.Floor(x / 3);
                return Bits(a) ^ Bits(b);
            }

            // A static field an argument reads is stored between the calls.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int StaticStored(int input)
            {
                var a = Math.Ceiling(s_x * 1.5);
                s_x = input + 0.75;
                var b = Math.Ceiling(s_x * 1.5);
                return Bits(a) ^ Bits(b);
            }

            // A call between the two writes the static an argument reads.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int CallStores()
            {
                var a = Math.Sqrt(s_x);
                Bump();
                var b = Math.Sqrt(s_x);
                return Bits(a) ^ Bits(b);
            }

            // A store through a ref that aliases the static an argument reads.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int RefStores(ref double y, int input)
            {
                var a = Math.Round(s_y * 2.5);
                y = input + 9.5;
                var b = Math.Round(s_y * 2.5);
                return Bits(a) ^ Bits(b);
            }

            // The local an argument reads has its address taken and is written through it.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int AddressTaken(double x)
            {
                ref var alias = ref x;
                var a = Math.Abs(x - 10);
                alias += 4;
                var b = Math.Abs(x - 10);
                return Bits(a) ^ Bits(b);
            }

            // The repeat is in a loop whose head is a jump target, and the loop changes the argument.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int RepeatInLoop(double x)
            {
                var first = Math.Sqrt(x);
                var total = 0.0;
                for (var i = 0; i < 3; i++)
                {
                    total = total * 31 + Math.Sqrt(x);
                    x += 2;
                }
                return Bits(first) ^ Bits(total);
            }

            // The same function with different arguments.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int OtherArguments(double x)
            {
                var a = Math.FusedMultiplyAdd(x, 2, 1);
                var b = Math.FusedMultiplyAdd(x, 3, 1);
                return Bits(a) ^ Bits(b) * 3;
            }

            // Interlocked writes the static an argument reads.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int InterlockedStores(int input)
            {
                var a = Math.Sqrt(s_y);
                Interlocked.Exchange(ref s_y, input + 30.25);
                var b = Math.Sqrt(s_y);
                return Bits(a) ^ Bits(b);
            }

            // An exception handler between the calls changes the argument.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int HandlerStores(double x, int input)
            {
                var a = Math.Floor(x * 5);
                try
                {
                    if (input % 2 == 0) throw new InvalidOperationException();
                }
                catch (InvalidOperationException)
                {
                    x = 100.75;
                }
                var b = Math.Floor(x * 5);
                return Bits(a) ^ Bits(b);
            }

            // A float repeat.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int FloatRepeat(float x)
            {
                var a = MathF.Sqrt(x * 3);
                var b = MathF.Sqrt(x * 3);
                return BitConverter.SingleToInt32Bits(a + b * 2) % 1000003;
            }

            // A repeat past another pure call and a store to a local the arguments do not read.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int PastAPureCall(double x)
            {
                var a = Math.Sqrt(x * 3);
                var c = Math.Floor(x / 2);
                var b = Math.Sqrt(x * 3);
                return Bits(a + b * 2 + c);
            }

            public static int Run(int input)
            {
                var which = input / 100; input %= 100;
                var x = input + 0.5;
                s_x = 2.25; s_y = 6.25;
                return which switch
                {
                    0 => LocalStored(x),
                    1 => ArgumentStored(x, input),
                    2 => StaticStored(input),
                    3 => CallStores(),
                    4 => RefStores(ref s_y, input),
                    5 => AddressTaken(x),
                    6 => RepeatInLoop(x),
                    7 => OtherArguments(x),
                    8 => InterlockedStores(input),
                    9 => HandlerStores(x, input),
                    10 => FloatRepeat((float)x),
                    11 => PastAPureCall(x),
                    _ => -1,
                };
            }
        }

        """;

    // As desktop .NET 11 computes them.
    private static readonly (int Input, int Expected)[] Cases = [(0, 573681), (1, 793170), (6, 176256), (100, 225526), (101, 143745), (106, 157550), (200, 660785), (201, 322714), (206, 708346), (300, 10765), (301, 10765), (306, 10765), (400, 310682), (401, 145375), (406, 969148), (500, 825525), (501, 343792), (506, 908107), (600, 221643), (601, 513207), (606, 114464), (700, 2200015), (701, 977692), (706, 2852190), (800, 893274), (801, 159030), (806, 369824), (900, 533666), (901, 0), (906, 45753), (1000, 760834), (1001, 84756), (1006, 18302), (1100, 12158), (1101, 944260), (1106, 567295)];

    [Theory]
    [InlineData(ManagedPureCallReuse.Auto)]
    [InlineData(ManagedPureCallReuse.Enabled)]
    [InlineData(ManagedPureCallReuse.Disabled)]
    public void ReusesAPureCallOnlyWhileItsArgumentsHold(ManagedPureCallReuse reuse) =>
        LegalityCases.Run($"PureCallReuseLegality{reuse}", Source, Cases, FinalWasmOptimization.O3,
            new WasmOptimizationOverrides(PureCallReuse: reuse));
}
