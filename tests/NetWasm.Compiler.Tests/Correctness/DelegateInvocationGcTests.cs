namespace NetWasm.Compiler.Tests.Correctness;

[Collection(CorrectnessTestGroup.Name)]
public sealed class DelegateInvocationGcTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void CustomDelegateCallsPreserveCallerReferencesAcrossCollections() =>
        runner.Run(new("DelegateInvocationRoots", "DelegateInvocationRoots", Source, [42])
        {
            ExpectedReturnValue = 42,
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection |
                OracleRuntimeCapabilities.Finalization,
            Matrix = new(CorpusMatrixProfile.Extended, Backend: CorpusExecutionBackend.Linked),
        });

    // A multicast delegate's later targets still read the arguments after an earlier
    // one collects, and a target's type initializer can collect before the target
    // reads them. The caller roots the arguments it passes for the whole call, and
    // the invoker roots what it reads itself.
    [Fact]
    public void InvokerKeepsArgumentsAcrossMulticastTargetsAndTypeInitializers() =>
        runner.Run(new("DelegateInvokerRoots", "DelegateInvokerRoots", InvokerSource, [42])
        {
            ExpectedReturnValue = 42,
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection |
                OracleRuntimeCapabilities.Finalization,
            Matrix = new(CorpusMatrixProfile.Extended, Backend: CorpusExecutionBackend.Linked),
        });

    private const string InvokerSource = """
        using System;
        using System.Runtime.CompilerServices;
        namespace DelegateInvokerRoots;

        public delegate int Probe(Witness witness, string label);

        // Each witness sets its own bit when finalized; one call's witness may be
        // collected once that call returns.
        public sealed class Witness
        {
            public static int Finalized;
            public int Id;
            public int Value;
            ~Witness() => Finalized |= 1 << Id;
        }

        public static class Initialized
        {
            private static readonly int Ready = Setup();

            static Initialized()
            {
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Setup()
            {
                EntryPoint.Collect();
                return 1;
            }

            public static int Read(Witness witness, string label) => witness.Value + label.Length - Ready;
        }

        public static class EntryPoint
        {
            public static int Trace() => 0;

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static void Collect()
            {
                for (var round = 0; round < 3; round++)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int CollectFirst(Witness witness, string label)
            {
                Collect();
                return -1;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Read(Witness witness, string label) => witness.Value + label.Length;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static Witness Fresh(int id, int value) => new() { Id = id, Value = value };

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static string Label() => new('x', 1);

            public static int Run(int input)
            {
                Probe multicast = CollectFirst;
                multicast += Read;
                if (multicast(Fresh(1, input - 1), Label()) != input || (Witness.Finalized & 2) != 0) return 1;
                Probe initialized = Initialized.Read;
                if (initialized(Fresh(2, input), Label()) != input || (Witness.Finalized & 4) != 0) return 2;
                return input;
            }
        }
        """;

    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;
        namespace DelegateInvocationRoots;

        public delegate void Callback();
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
            private static void Collect()
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Invoke(Callback callback) => callback();

            public static int Run(int input)
            {
                Callback callback = Collect;
                var witness = new Witness { Value = input };
                object first = input;
                callback();
                if (Witness.Finalized != 0 || witness.Value != input || (int)first != input) return 1;
                object second = input + 1;
                Invoke(callback);
                if (Witness.Finalized != 0 || witness.Value != input || (int)second != input + 1) return 2;
                return input;
            }
        }
        """;
}
