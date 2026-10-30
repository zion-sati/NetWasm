using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tests.Correctness;

[Collection(CorrectnessTestGroup.Name)]
public sealed class LoopRotationCompatibilityTests(CorrectnessTestRunner runner)
{
    // O3 tests a loop's condition before entry and at the bottom of each
    // iteration, and moves the condition's first array null check to the
    // loop entry; Oz rotates only conditions without calls. Every loop shape
    // and exception must match desktop .NET.
    [Theory]
    [InlineData("Release-Wasm32-Direct-Linked", FinalWasmOptimization.O3)]
    [InlineData("Release-Wasm64-Direct-Linked", FinalWasmOptimization.O3)]
    [InlineData("Release-Wasm32-Direct-Linked", FinalWasmOptimization.Oz)]
    public void RotatedLoopsAndEntryNullChecksMatchDesktop(string cell, FinalWasmOptimization optimization) =>
        runner.Run(new("RotatedLoops", "RotatedLoops", Source, [0, 3, 17])
        {
            Optimization = optimization,
            Matrix = new(CorpusMatrixProfile.Extended, cell, CorpusExecutionBackend.Linked),
        });

    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;
        namespace RotatedLoops;

        public static class EntryPoint
        {
            private static int s_effects;
            public static int Trace() => 0;

            public static int Run(int count)
            {
                var values = new int[count];
                for (var index = 0; index < values.Length; index++) values[index] = index * 3 + 1;
                var total = Sum(values) + Skips(values) + Nested(values, count) + DoWhile(count) + Countdown(values) +
                    Calls(values);
                total += Lengths(values, null) + Lengths(values, new int[count / 2]) + ZeroTrip(null, 0);
                try { total += Sum(null!); } catch (NullReferenceException) { total += 1000; }
                try { total += Effects(null!); } catch (NullReferenceException) { total += 2000 + s_effects; }
                try { total += Elements(values, count + 1); } catch (IndexOutOfRangeException) { total += 4000; }
                try { total += ZeroTrip(null, 1); } catch (NullReferenceException) { total += 8000; }
                return total;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Sum(int[] values)
            {
                var sum = 0;
                for (var index = 0; index < values.Length; index++) sum += values[index];
                return sum;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Skips(int[] values)
            {
                var sum = 0;
                for (var index = 0; index < values.Length; index++)
                {
                    if ((values[index] & 1) == 0) continue;
                    if (values[index] > 40) break;
                    sum += values[index];
                }
                return sum;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Nested(int[] values, int count)
            {
                var sum = 0;
                for (var outer = 0; outer < count; outer++)
                    for (var index = 0; index < values.Length; index++)
                    {
                        if (index == outer) continue;
                        sum += values[index] * outer;
                    }
                return sum;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int DoWhile(int count)
            {
                var index = 0;
                var sum = 0;
                do { sum += index; index++; } while (index < count);
                return sum;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Countdown(int[] values)
            {
                var index = values.Length;
                var sum = 0;
                while (index > 0) { index--; sum += values[index]; }
                return sum;
            }

            // The condition reads one array's length, then possibly another's.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Lengths(int[] values, int[]? other)
            {
                var sum = 0;
                for (var index = 0; index < values.Length && (other == null || index < other.Length); index++) sum++;
                return sum;
            }

            // A store before the loop happens before its null check.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Effects(int[] values)
            {
                s_effects = 7;
                var sum = 0;
                for (var index = 0; index < values.Length; index++) sum += values[index];
                return sum;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Elements(int[] values, int limit)
            {
                var sum = 0;
                for (var index = 0; index < limit; index++) sum += values[index];
                return sum;
            }

            // A condition that calls, which Oz leaves unrotated.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Calls(int[] values)
            {
                var index = 0;
                var sum = 0;
                while (Advance(values, ref index)) sum += values[index - 1];
                return sum;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static bool Advance(int[] values, ref int index) => index++ < values.Length;

            // The condition never touches the array, so a null one is fine until read.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ZeroTrip(int[]? values, int count)
            {
                var sum = 0;
                for (var index = 0; index < count; index++) sum += values![index];
                return sum;
            }
        }
        """;
}
