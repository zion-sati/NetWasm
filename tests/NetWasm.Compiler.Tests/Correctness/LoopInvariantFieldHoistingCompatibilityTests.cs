using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class LoopInvariantFieldHoistingCompatibilityTests(
    CorrectnessTestRunner runner)
{
    [Fact]
    public void StableAggregateFieldsSurviveCollectionInTheFirstO3ConsumerCell() =>
        runner.Run(Fixture("Release-Wasm32-Direct-Linked"));

    [Fact]
    public void StableAggregateFieldsMatchDesktopAcrossProfilesTargetsAndForms() =>
        runner.Run(Fixture());

    private static CorpusFixture Fixture(string? cell = null) => new(
        "LoopInvariantFieldHoistingCompatibility",
        "NetWasm.Correctness.LoopInvariantFieldHoisting",
        Source,
        [0, 1, 4, 17])
    {
        ExpectedReturnValues = new Dictionary<int, int>
        {
            [0] = 0,
            [1] = 101,
            [4] = 416,
            [17] = 1989,
        }.ToImmutableDictionary(),
        RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection,
        Matrix = new(
            CorpusMatrixProfile.Extended,
            cell,
            CorpusExecutionBackend.Linked),
        Optimization = FinalWasmOptimization.O3,
        OracleMode = OracleMode.SameSource,
        SameSourceReason =
            "Loop-invariant field hoisting must preserve ordinary value snapshots and managed roots.",
    };

    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;

        namespace NetWasm.Correctness.LoopInvariantFieldHoisting;

        public readonly record struct Accepted(int[] Values, int Count);

        public static class EntryPoint
        {
            public static int Trace() => 0;

            public static int Run(int input)
            {
                var accepted = Create(input);
                var sum = 0;
                for (var index = 0; index < accepted.Count; index++)
                {
                    if (index == 0 && !Collect())
                    {
                        return -1;
                    }
                    sum = unchecked(sum + accepted.Values[index]);
                }
                return unchecked(sum + accepted.Count * 100);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static Accepted Create(int count)
            {
                var values = new int[count];
                for (var index = 0; index < values.Length; index++)
                {
                    values[index] = index * 2 + 1;
                }
                return new Accepted(values, values.Length);
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
