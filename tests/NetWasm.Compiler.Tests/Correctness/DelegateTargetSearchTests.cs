namespace NetWasm.Compiler.Tests.Correctness;

// A delegate type bound to more than a few methods finds its target by binary
// search on the target's function index. Every target, static or instance, on
// a class or a struct, multicast or not, must still be the one that runs.
[Collection(CorrectnessTestGroup.Name)]
public sealed class DelegateTargetSearchTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void DelegatesWithManyTargetsInvokeTheBoundMethod() =>
        runner.Run(new(
            "DelegateTargetSearch",
            "NetWasm.Correctness.DelegateTargetSearch",
            """
            using System;

            namespace NetWasm.Correctness.DelegateTargetSearch;

            public delegate int Step(int value);

            public sealed class Scaler(int factor)
            {
                public int Scale(int value) => value * factor;
                public int Shift(int value) => value + factor;
            }

            public struct Offset(int amount)
            {
                public int Apply(int value) => value - amount;
            }

            public static class EntryPoint
            {
                private static int s_calls;

                public static int Run(int input)
                {
                    var scaler = new Scaler(input + 3);
                    var captured = input * 7;
                    Step[] steps =
                    [
                        static value => value + 1, static value => value + 2, static value => value * 3,
                        static value => value - 4, static value => value ^ 5, static value => value << 1,
                        static value => value >> 1, static value => -value, static value => value % 7,
                        static value => value | 8, static value => value & 9, static value => value + 10,
                        value => value + captured, value => value * captured, value => captured - value,
                        scaler.Scale, scaler.Shift, new Offset(input).Apply, Counted, Triple,
                    ];
                    var hash = 17;
                    foreach (var step in steps)
                    {
                        hash = unchecked(hash * 31 + step(input + 11));
                    }
                    // A multicast delegate returns its last target's result and runs every one.
                    var chain = steps[3] + steps[15] + Counted + steps[(input & 7) + 2];
                    hash = unchecked(hash * 31 + chain(input));
                    return unchecked(hash * 31 + s_calls);
                }

                private static int Counted(int value)
                {
                    s_calls++;
                    return value + s_calls;
                }

                private static int Triple(int value) => value * 3;

                public static int Trace() => 0;
            }
            """,
            [0, 1, 2, 5, 9]));
}
