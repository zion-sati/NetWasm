using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class AggregateReferenceLaneGcTests(CorrectnessTestRunner runner)
{
    [Theory]
    [InlineData("Release-Wasm32-Direct-Linked")]
    [InlineData("Release-Wasm64-Direct-Linked")]
    public void PromotedReferenceLanesKeepTheirObjectsAlive(string cell) =>
        runner.Run(new("ReferenceLaneRoots", "ReferenceLaneRoots", Source, [0, 3])
        {
            ExpectedReturnValue = 42,
            Optimization = FinalWasmOptimization.O3,
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection |
                OracleRuntimeCapabilities.WeakReferenceClearing,
            Matrix = new(CorpusMatrixProfile.Extended, cell, CorpusExecutionBackend.Linked),
        });

    // O3 keeps a constructed span and a struct holding an object in lane
    // locals rather than in their frame storage, so each array below is
    // reachable only through a lane while the collection runs. A freed span
    // target is reused by arrays filled with -1; a freed holder target clears
    // its weak reference.
    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;
        namespace ReferenceLaneRoots;

        public struct Holder
        {
            public object Value;
            public int Count;
        }

        public static class EntryPoint
        {
            private static WeakReference<int[]> s_weak = null!;
            public static int Trace() => 0;

            public static int Run(int seed)
            {
                for (var round = 0; round < 3; round++)
                {
                    var start = seed + round;
                    if (Spans(start) != 64 * start + 2016) return 1;
                    if (References(start) != 2 * start + 3) return 2;
                }
                return 42;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Spans(int seed)
            {
                ReadOnlySpan<int> span = Fill(seed, 64);
                if (!Collect()) return -1;
                Reuse();
                var sum = 0;
                for (var index = 0; index < span.Length; index++) sum += span[index];
                return sum;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int References(int seed)
            {
                // Computed first, so that no call runs while the holder's address is on the stack.
                object value = Track(Fill(seed, 2));
                Holder holder;
                holder.Count = 2;
                holder.Value = value;
                if (!Collect() || !s_weak.TryGetTarget(out _)) return -1;
                var values = (int[])holder.Value;
                return values[0] + values[1] + holder.Count;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int[] Fill(int seed, int length)
            {
                var values = new int[length];
                for (var index = 0; index < values.Length; index++) values[index] = seed + index;
                return values;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int[] Track(int[] values)
            {
                s_weak = new WeakReference<int[]>(values);
                return values;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Reuse()
            {
                var live = new int[1024][];
                for (var index = 0; index < live.Length; index++)
                {
                    live[index] = new int[(index & 1) == 0 ? 64 : 2];
                    live[index].AsSpan().Fill(-1);
                }
                GC.KeepAlive(live);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static bool Collect()
            {
                var before = GC.CollectionCount(0);
                GC.Collect();
                return GC.CollectionCount(0) > before;
            }
        }
        """;
}
