namespace NetWasm.Compiler.Tests.Correctness;

public sealed class InterlockedExceptionGcTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void NullGenericCompareExchangePreservesReferencesUsedByACollectingFilter() =>
        runner.Run(new("InterlockedExceptionRoots", "InterlockedExceptionRoots", Source, [42])
        {
            ExpectedReturnValue = 42,
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection |
                OracleRuntimeCapabilities.Finalization,
            Matrix = new(CorpusMatrixProfile.Extended, Backend: CorpusExecutionBackend.Linked),
        });

    [Fact]
    public void ThrowingFilterPreservesItsNewLocalBeforeAnUnexecutedOverwrite() =>
        runner.Run(new("InterlockedRejectedFilterRoots", "InterlockedRejectedFilterRoots", RejectedSource, [42])
        {
            ExpectedReturnValue = 42,
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection |
                OracleRuntimeCapabilities.Finalization,
            Matrix = new(CorpusMatrixProfile.Extended, Backend: CorpusExecutionBackend.Linked),
        });

    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;
        using System.Threading;
        namespace InterlockedExceptionRoots;

        public sealed class Witness
        {
            public static int Finalized;
            public int Value;
            ~Witness() => Finalized++;
        }

        public static class EntryPoint
        {
            public static int Trace() => 0;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static bool Collect()
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                return true;
            }

            public static int Run(int input)
            {
                var witness = new Witness { Value = input };
                try
                {
                    Interlocked.CompareExchange<string?>(ref Unsafe.NullRef<string?>(), "next", null);
                    return 1;
                }
                catch (NullReferenceException) when (Collect())
                {
                    if (Witness.Finalized != 0 || witness.Value != input) return 2;
                }
                return input;
            }
        }
        """;

    private const string RejectedSource = """
        using System;
        using System.Runtime.CompilerServices;
        using System.Threading;
        namespace InterlockedRejectedFilterRoots;

        public sealed class Witness
        {
            public static int Finalized;
            public int Value;
            ~Witness() => Finalized++;
        }

        public static class EntryPoint
        {
            public static int Trace() => 0;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static Witness CollectAndThrow()
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                throw new InvalidOperationException("reject filter");
            }

            public static int Run(int input)
            {
                Witness? witness = null;
                try
                {
                    Interlocked.CompareExchange<string?>(ref Unsafe.NullRef<string?>(), "next", null);
                    return 1;
                }
                // The filter creates the only reference after the original throw.
                // Its later assignment must not kill that reference while the
                // collecting call can throw and skip the assignment altogether.
                catch (NullReferenceException) when (
                    (witness = new Witness { Value = input }).Value == input &&
                    (witness = CollectAndThrow()) != null)
                {
                    return 2;
                }
                catch (NullReferenceException)
                {
                    if (Witness.Finalized != 0 || witness is null || witness.Value != input) return 3;
                }
                return input;
            }
        }
        """;
}
