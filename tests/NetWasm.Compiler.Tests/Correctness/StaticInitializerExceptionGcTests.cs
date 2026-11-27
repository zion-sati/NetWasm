// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

// A static field access that may run its type's initializer keeps the references live across it, under GC stress
// (a collection at every allocation) with both collectors.
public sealed class StaticInitializerExceptionGcTests(CorrectnessTestRunner runner)
{
    // The initializer allocates and then throws the first time the field is read inside a protected block, a filter
    // collects before the handler runs, and the witness the handler reads survives. A value live into a handler is a
    // local, whose root slot every store writes (AuthoritativeRootSourceSelector), so this holds the throwing path to
    // desktop's result rather than the access's own publication, which the next test and RootMapTests pin.
    [Fact]
    public void AThrowingInitializerInAProtectedBlockKeepsTheHandlersReferences() =>
        runner.Run(new("StaticInitializerExceptionRoots", "StaticInitializerExceptionRoots", Source, [42])
        {
            ExpectedReturnValue = 42,
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection |
                OracleRuntimeCapabilities.Finalization,
            Matrix = new(CorpusMatrixProfile.Extended, Backend: CorpusExecutionBackend.Linked),
        });

    // The witness is on the evaluation stack, where no authoritative slot holds it, while the access runs an
    // initializer that allocates: the access must publish it.
    [Fact]
    public void AnAllocatingInitializerKeepsTheEvaluationStacksReferences() =>
        runner.Run(new("StaticInitializerStackRoots", "StaticInitializerStackRoots", StackSource, [42])
        {
            ExpectedReturnValue = 42,
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection,
            Matrix = new(CorpusMatrixProfile.Extended, Backend: CorpusExecutionBackend.Linked),
        });

    private const string StackSource = """
        using System;
        using System.Runtime.CompilerServices;
        namespace StaticInitializerStackRoots;

        public sealed class Witness
        {
            public int Value;
        }

        public static class Allocating
        {
            public static Witness[] Decoys;

            // An explicit static constructor: the type is precise, so it initializes at the access.
            static Allocating() => Decoys = Fill();

            // Allocates enough that a collection runs, then fills what any freed object left.
            private static Witness[] Fill()
            {
                GC.Collect();
                var decoys = new Witness[64];
                for (var i = 0; i < decoys.Length; i++) decoys[i] = new Witness { Value = -1 };
                return decoys;
            }
        }

        public static class EntryPoint
        {
            public static int Trace() => 0;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static Witness Create(int value) => new Witness { Value = value };

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Read(Witness witness, Witness[] decoys) => decoys.Length == 64 ? witness.Value : 0;

            public static int Run(int input) => Read(Create(input), Allocating.Decoys);
        }
        """;

    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;
        namespace StaticInitializerExceptionRoots;

        public sealed class Witness
        {
            public static int Finalized;
            public int Value;
            ~Witness() => Finalized++;
        }

        public static class Cache
        {
            public static readonly InvalidOperationException Failure = new("cached");
        }

        public static class Throwing
        {
            public static object[]? Scratch;
            public static int Field;

            // An explicit static constructor: the type is precise, so it initializes at the access.
            static Throwing() => Field = Fail();

            // Allocates before it throws, so a collection can run while the access is under way.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Fail()
            {
                Scratch = new object[16];
                throw Cache.Failure;
            }
        }

        public static class EntryPoint
        {
            public static int Trace() => 0;

            public static Witness[] Decoys = new Witness[0];

            // The witness comes back from a call, so no root slot holds it until a safepoint where it is live.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static Witness Create(int value) => new Witness { Value = value };

            // A collection, then objects that take the memory of any it freed.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static bool Collect()
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                var decoys = new Witness[64];
                for (var i = 0; i < decoys.Length; i++) decoys[i] = new Witness { Value = -1 };
                Decoys = decoys;
                return true;
            }

            public static int Run(int input)
            {
                if (Cache.Failure is null) return 1;
                var witness = Create(input);
                try
                {
                    return Throwing.Field + 1;
                }
                // NetWasm rethrows the initializer's own exception where desktop wraps it in a
                // TypeInitializationException, so the filter takes either.
                catch (Exception) when (Collect())
                {
                    if (Witness.Finalized != 0 || witness.Value != input) return 2;
                }
                return input;
            }
        }
        """;
}
