using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class ScalarCallPreservationGcTests(CorrectnessTestRunner runner)
{
    [Theory]
    [InlineData("Release-Wasm32-Direct-Linked")]
    [InlineData("Release-Wasm64-Direct-Linked")]
    public void FloatingPreservationStorageDoesNotDisplaceLiveRoots(string cell) =>
        runner.Run(new("FloatingCallRoots", "FloatingCallRoots", Source, [0, 3])
        {
            ExpectedReturnValue = 42,
            Optimization = FinalWasmOptimization.O3,
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection |
                OracleRuntimeCapabilities.WeakReferenceClearing,
            Matrix = new(CorpusMatrixProfile.Extended, cell, CorpusExecutionBackend.Linked),
        });

    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;
        namespace FloatingCallRoots;

        public sealed class Payload { public int Value; }

        public static class EntryPoint
        {
            private static WeakReference<Payload> s_weak = null!;
            public static int Trace() => 0;

            public static int Run(int count)
            {
                var outer = new Payload { Value = 19 };
                if (Probe(count, false) != 42) return 1;
                var caught = false;
                try { Probe(count, true); }
                catch (InvalidOperationException) { caught = true; }
                if (!caught || Probe(count, false) != 42) return 2;
                GC.Collect();
                return outer.Value == 19 ? 42 : 3;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Probe(int count, bool fail)
            {
                var live = new Payload { Value = 37 };
                s_weak = new WeakReference<Payload>(live);
                float single = -0.5f;
                double number = 1.25;
                long integer = long.MaxValue - 1;
                for (var i = 0; i < count; i++)
                {
                    single += 0.5f;
                    number += 0.25;
                    integer = unchecked(integer + 3);
                }
                var collected = Collect(fail);
                if (!collected || single != -0.5f + count * 0.5f ||
                    number != 1.25 + count * 0.25 ||
                    integer != unchecked(long.MaxValue - 1 + count * 3L)) return 4;
                if (!s_weak.TryGetTarget(out var target) || !ReferenceEquals(target, live)) return 5;
                return live.Value == 37 ? 42 : 6;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static bool Collect(bool fail)
            {
                var before = GC.CollectionCount(0);
                GC.Collect();
                if (fail) throw new InvalidOperationException();
                return GC.CollectionCount(0) > before;
            }
        }
        """;
}
