// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Tests.Correctness;

namespace NetWasm.Compiler.Tests;

// A finally runs after the exception has left every method it passed through, so a collection inside it must not keep
// what only those methods' frames held: desktop .NET no longer reports their frames in the second pass, and NetWasm's
// landing pad returns the frame arenas to where they stood as the protected block began, for a block with catch clauses
// (its exception frame's marks) and one without (the marks its method keeps in locals). The thrower holds one witness in
// its root frame and one in a struct whose address it takes, which lives in its value frame, across the call that
// throws; without the rewind, either stays alive.
public sealed class CleanupRegionRewindGcTests(CorrectnessTestRunner runner)
{
    internal const string Source = """
        using System;
        using System.Runtime.CompilerServices;

        namespace CleanupRegionRewindFixture;

        public struct Holder
        {
            public object Value;
        }

        public static class EntryPoint
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Collect()
            {
                GC.Collect();
                GC.Collect();
            }

            private static bool s_throw;

            // Throws only when the input says so, so the compiler cannot treat the call as one that never returns,
            // and the references used after it are live across it.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void ThrowNow()
            {
                if (s_throw) throw new InvalidOperationException();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Keep(object value) { }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Fill(ref Holder holder, WeakReference[] weak)
            {
                holder.Value = new object();
                weak[1] = new WeakReference(holder.Value);
            }

            // Both witnesses are live across the call that throws: one in the root frame, one in the value frame.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Thrower(WeakReference[] weak)
            {
                var witness = new object();
                weak[0] = new WeakReference(witness);
                var holder = new Holder();
                Fill(ref holder, weak);
                ThrowNow();
                Keep(witness);
                Keep(holder.Value);
            }

            // A finally, whose block has no catch clause.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int AliveInFinally(WeakReference[] weak)
            {
                var alive = -1;
                try
                {
                    try
                    {
                        Thrower(weak);
                    }
                    finally
                    {
                        Collect();
                        alive = (weak[0].IsAlive ? 1 : 0) + (weak[1].IsAlive ? 2 : 0);
                    }
                }
                catch (InvalidOperationException)
                {
                }
                return alive;
            }

            // A catch handler of the block the exception left.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int AliveInCatch(WeakReference[] weak)
            {
                try
                {
                    Thrower(weak);
                    return -1;
                }
                catch (InvalidOperationException)
                {
                    Collect();
                    return (weak[0].IsAlive ? 1 : 0) + (weak[1].IsAlive ? 2 : 0);
                }
            }

            public static int Run(int input)
            {
                s_throw = input > 0;
                var inFinally = AliveInFinally(new WeakReference[2]);
                if (inFinally != 0) return 10 + inFinally;
                var inCatch = AliveInCatch(new WeakReference[2]);
                if (inCatch != 0) return 20 + inCatch;
                return input + 1;
            }
        }
        """;

    [Theory]
    [InlineData("Debug-Wasm32-Direct-Linked", FinalWasmOptimization.None)]
    [InlineData("Release-Wasm32-Direct-Linked", FinalWasmOptimization.None)]
    [InlineData("Release-Wasm32-Direct-Linked", FinalWasmOptimization.O3)]
    [InlineData("Debug-Wasm64-Direct-Linked", FinalWasmOptimization.None)]
    [InlineData("Release-Wasm64-Direct-Linked", FinalWasmOptimization.O3)]
    public void ACollectionInAHandlerDoesNotKeepWhatOnlyTheThrowersFramesHeld(string cell, FinalWasmOptimization optimization)
    {
        var fixture = new CorpusFixture("CleanupRegionRewindFixture", "CleanupRegionRewindFixture", Source, [41])
        {
            CaseId = "frame.cleanup-region-rewind",
            ExpectedReturnValue = 42,
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection,
            Matrix = new(CorpusMatrixProfile.Extended, cell, CorpusExecutionBackend.Linked),
            ExposesLegacyTrace = false,
            OracleMode = OracleMode.SameSource,
            SameSourceReason = "Compare collections in a finally and a catch handler, after the exception left a thrower holding references, against the desktop runtime.",
            Optimization = optimization,
        };
        runner.Run(fixture);
    }
}
