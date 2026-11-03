using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;

namespace NetWasm.Compiler.Tests.Legality;

// A for loop over a few constant values runs its body once per value in a
// row only when nothing but the loop counts its counter and its branches stay
// in shape, and then leaves the counter where the loop would. Each method
// breaks one condition or leaves the loop early: the body stores the counter
// or passes it by reference, an inner loop branches backward, a break, a
// continue, a return, a throw caught around the loop, a switch and a try in
// the body, and the counter read after the loop and after a range that runs
// no iteration. A loop in a try with a filtered handler unrolls; a filtered
// try in the body keeps the loop, as do increments other than i++.
public sealed class ConstantTripLoopUnrollingLegalityTests
{
    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;

        public static class EntryPoint
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Bump(ref int value) => value += 2;

            // The body stores the counter.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int CounterStored(int input)
            {
                var total = 0;
                for (var i = 0; i < 6; i++)
                {
                    total = total * 3 + i;
                    if (i == input % 3) i++;
                }
                return total;
            }

            // The counter's address is taken.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int CounterByAddress(int input)
            {
                var total = 0;
                for (var i = 0; i < 6; i++)
                {
                    total = total * 3 + i;
                    if (i == input % 4) Bump(ref i);
                }
                return total;
            }

            // An inner loop branches backward within the body.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int InnerLoop(int input)
            {
                var total = 0;
                for (var i = 0; i < 3; i++)
                {
                    for (var j = 0; j < i + input % 3; j++) total += j;
                }
                return total;
            }

            // A break leaves the counter where it stopped.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int BreakValue(int input)
            {
                int i;
                var total = 0;
                for (i = 1; i < 7; i++)
                {
                    if (i * input > 10) break;
                    total += i;
                }
                return total * 100 + i;
            }

            // A continue moves on to the next value.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ContinueSkips(int input)
            {
                var total = 0;
                for (var i = 0; i < 8; i++)
                {
                    if ((i + input) % 3 == 0) continue;
                    total = total * 2 + i;
                }
                return total;
            }

            // A return inside the body.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ReturnInside(int input)
            {
                for (var i = 2; i <= 6; i++)
                {
                    if (i * i > input * 4) return i * 10;
                }
                return -3;
            }

            // A throw leaves the counter for the handler around the loop.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ThrowInside(int input)
            {
                var i = 0;
                try
                {
                    for (i = 0; i < 5; i++)
                    {
                        if (i == input % 5) throw new InvalidOperationException();
                    }
                    return -4;
                }
                catch (InvalidOperationException) { return i * 7; }
            }

            // A switch inside the body.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int SwitchInside(int input)
            {
                var total = 0;
                for (var i = 0; i < 2; i++)
                {
                    switch (i + input)
                    {
                        case 0: total += 1; break;
                        case 1: total *= 3; break;
                        case 2: total -= 2; break;
                        case 3: total ^= 5; break;
                    }
                }
                return total;
            }

            // A try inside the body.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int TryInside(int input)
            {
                var total = 0;
                for (var i = 0; i < 4; i++)
                {
                    try { total = total * 3 + 12 / (i - input % 4); }
                    catch (DivideByZeroException) { total += 100; }
                }
                return total;
            }

            // The counter is read after the loop, and a range that runs no iteration.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int AfterTheLoop(int input)
            {
                int i, j;
                var total = 0;
                for (i = 3; i <= 5; i++) total = total * 4 + i + input;
                for (j = 5; j < 3; j++) total = -1;
                return total * 100 + i * 10 + j;
            }

            // The loop in a try whose handler a filter guards.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int FilterAround(int input)
            {
                var total = 0;
                try
                {
                    for (var i = 0; i < 4; i++) total = total * 3 + 12 / (i - input % 5);
                }
                catch (DivideByZeroException) when (total < 30) { total += 100; }
                catch (DivideByZeroException) { total -= 100; }
                return total;
            }

            // A filtered try inside the body.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int FilterInside(int input)
            {
                var total = 0;
                for (var i = 0; i < 4; i++)
                {
                    try { total = total * 3 + 12 / (i - input % 4); }
                    catch (DivideByZeroException) when (i % 2 == 0) { total += 100; }
                    catch (DivideByZeroException) { total -= 50; }
                }
                return total;
            }

            // Increments other than i++: 1 + i, a step of two, a doubling, and a store to another local.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int OtherIncrements(int input)
            {
                var total = 0;
                for (var i = 0; i < 4; i = 1 + i) total = total * 3 + i + input;
                for (var i = 0; i < 8; i += 2) total = total * 5 + i;
                for (var i = 1; i < 16; i <<= 1) total = total * 7 + i;
                for (int i = 0, j = 0; i < 4; j = i + 1)
                {
                    total += j;
                    i++;
                }
                return total;
            }

            public static int Run(int input)
            {
                var which = input / 100; input %= 100;
                return which switch
                {
                    0 => CounterStored(input),
                    1 => CounterByAddress(input),
                    2 => InnerLoop(input),
                    3 => BreakValue(input),
                    4 => ContinueSkips(input),
                    5 => ReturnInside(input),
                    6 => ThrowInside(input),
                    7 => SwitchInside(input),
                    8 => TryInside(input),
                    9 => AfterTheLoop(input),
                    10 => FilterAround(input),
                    11 => FilterInside(input),
                    12 => OtherIncrements(input),
                    _ => -9,
                };
            }
        }

        """;

    // As desktop .NET 11 computes them.
    private static readonly (int Input, int Expected)[] Cases = [(0, 98), (1, 71), (2, 62), (3, 98), (6, 98), (100, 44), (101, 26), (102, 20), (103, 18), (106, 20), (200, 1), (201, 4), (202, 10), (203, 1), (206, 1), (300, 2107), (301, 2107), (302, 1506), (303, 604), (306, 102), (400, 65), (401, 75), (402, 44), (403, 65), (406, 65), (500, 20), (501, 30), (502, 30), (503, 40), (506, 50), (600, 0), (601, 7), (602, 14), (603, 21), (606, 7), (700, 3), (701, -2), (702, -5), (703, 5), (706, 0), (800, 2830), (801, 834), (802, 222), (803, 34), (806, 222), (900, 6965), (901, 9065), (902, 11165), (903, 13265), (906, 19565), (1000, 100), (1001, 88), (1002, 70), (1003, 34), (1006, 88), (1100, 2830), (1101, -516), (1102, 222), (1103, -116), (1106, 222), (1200, 27194212), (1201, 87219212), (1202, 147244212), (1203, 207269212), (1206, 387344212)];

    [Theory]
    [InlineData(ManagedLoopUnrolling.Auto)]
    [InlineData(ManagedLoopUnrolling.Enabled)]
    [InlineData(ManagedLoopUnrolling.Disabled)]
    public void UnrollsAConstantRangeOnlyWhenTheLoopAloneCountsIt(ManagedLoopUnrolling unrolling) =>
        LegalityCases.Run($"ConstantTripLoopUnrollingLegality{unrolling}", Source, Cases, FinalWasmOptimization.O3,
            new WasmOptimizationOverrides(LoopUnrolling: unrolling));
}
