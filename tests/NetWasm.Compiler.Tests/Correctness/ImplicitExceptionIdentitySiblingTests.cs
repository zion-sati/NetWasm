namespace NetWasm.Compiler.Tests.Correctness;

// Siblings of issue 160, where every implicit NullReferenceException was one shared object: each
// kind of implicit exception (null references, indexes, division, overflow, casts, covariant
// array stores, float conversions), thrown twice from one site and once from
// another, is three objects of the kind desktop throws, and what one carries in Data stays its
// own across collections.
public sealed class ImplicitExceptionIdentitySiblingTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void EachImplicitExceptionIsAnObjectOfItsOwn() =>
        runner.Run(new("ImplicitExceptionIdentity", "ImplicitExceptionIdentity", Source, [0, 1])
        {
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection,
            CollectsAtEveryAllocation = true,
            Matrix = new(CorpusMatrixProfile.Extended, Backend: CorpusExecutionBackend.Linked),
        });

    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Runtime.CompilerServices;
        namespace ImplicitExceptionIdentity;

        public sealed class Box { public int Value; }

        public static class EntryPoint
        {
            public static int Trace() => 0;

            public static int Run(int input)
            {
                var kinds = new Func<int, int>[]
                {
                    NullField, NullArray, Index, StringIndex, Divide, Remainder, CheckedAdd, CheckedNarrow,
                    MinimumByMinusOne, Cast, Unbox, CovariantStore, FloatToInt,
                };
                var score = 0;
                for (var kind = 0; kind < kinds.Length; kind++)
                {
                    var thrown = new List<Exception>();
                    // Twice from one site, once from the other.
                    for (var round = 0; round < 3; round++)
                    {
                        try
                        {
                            kinds[round < 2 ? kind : (kind + input + 1) % kinds.Length](round);
                        }
                        catch (Exception exception)
                        {
                            exception.Data["round"] = new Box { Value = kind * 10 + round };
                            thrown.Add(exception);
                        }
                    }
                    GC.Collect();
                    for (var round = 0; round < thrown.Count; round++)
                    {
                        var box = (Box)thrown[round].Data["round"]!;
                        if (box.Value != kind * 10 + round || thrown[round].Data.Count != 1) return 1000 + kind * 10 + round;
                        for (var other = 0; other < round; other++)
                        {
                            if (ReferenceEquals(thrown[round], thrown[other])) return 2000 + kind * 10 + round;
                        }
                    }
                    score = score * 31 + Kind(thrown[0]) * 7 + Kind(thrown[2]) + thrown.Count;
                }
                return score;
            }

            private static int Kind(Exception exception) => exception switch
            {
                NullReferenceException => 1,
                IndexOutOfRangeException => 2,
                DivideByZeroException => 3,
                OverflowException => 4,
                InvalidCastException => 5,
                ArrayTypeMismatchException => 6,
                ArithmeticException => 7,
                _ => 8,
            };

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static T? Missing<T>() where T : class => null;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int NullField(int round) => Missing<Box>()!.Value + round;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int NullArray(int round) => Missing<int[]>()!.Length + round;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Index(int round) => (new int[2])[round + 2];

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int StringIndex(int round) => "ab"[round + 2];

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Divide(int round) => 10 / (round - round);

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Remainder(int round) => (int)(10L % (round - round));

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int CheckedAdd(int round) => checked(int.MaxValue - 1 + round + 2);

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int CheckedNarrow(int round) => checked((int)(long.MaxValue - round));

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int MinimumByMinusOne(int round) => int.MinValue / (round - round - 1);

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Cast(int round) => ((string)(object)new Box { Value = round }).Length;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Unbox(int round) => (int)(object)(long)round;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int CovariantStore(int round)
            {
                object[] values = new string[1];
                values[0] = new Box { Value = round };
                return values.Length;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int FloatToInt(int round) => checked((int)(double.NaN + round));
        }
        """;
}
