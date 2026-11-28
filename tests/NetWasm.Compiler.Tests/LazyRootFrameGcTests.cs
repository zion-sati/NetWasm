// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Tests.Correctness;

namespace NetWasm.Compiler.Tests;

// A method whose root frame is entered at its first publication rather than on entry, around protected blocks: a
// landing pad rewinds the frame arenas to the block's marks, which pops a frame entered inside the block, slots and all.
// So a root published in a try and used by its handler after a collection, in a catch and in a finally, must be held by
// a frame that predates the block; a root a handler publishes first must stay held after the try; a frame popped by the
// rewind must not be written again (the exception's cell is entered in its place); and a thousand throwing calls, plain
// and async (awaited directly and called normally), must leave no frame behind that keeps what they published alive. Each collection is followed by garbage that reuses freed memory, so a holder collected too early
// reads back wrong.
public sealed class LazyRootFrameGcTests(CorrectnessTestRunner runner)
{
    internal const string Source = """
        using System;
        using System.Runtime.CompilerServices;
        using System.Threading.Tasks;

        namespace LazyRootFrameFixture;

        public sealed class Holder
        {
            public int Value;

            public Holder(int value) => Value = value;
        }

        public static class EntryPoint
        {
            private static bool s_throw;

            // False at run time; each method's early return publishes nothing, so its root frame is entered lazily.
            private static bool s_fast;
            private static Holder? s_sink;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Collect()
            {
                GC.Collect();
                GC.Collect();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Churn()
            {
                for (var index = 0; index < 64; index++) s_sink = new Holder(-1 - index);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void ThrowNow()
            {
                if (s_throw) throw new InvalidOperationException();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Use(Holder holder)
            {
                if (holder.Value < 0) throw new ArgumentException();
            }

            // A root published inside the try, which the catch handler reads after a collection.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int InCatch(int value)
            {
                if (s_fast) return value;
                var keep = new Holder(value);
                try
                {
                    Use(keep);
                    ThrowNow();
                    Use(keep);
                }
                catch (InvalidOperationException)
                {
                    Collect();
                    Churn();
                    return keep.Value;
                }
                return -1;
            }

            // The same, read by a finally while the exception passes.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int InFinally(int value)
            {
                if (s_fast) return value;
                var keep = new Holder(value);
                var result = -1;
                try
                {
                    try
                    {
                        Use(keep);
                        ThrowNow();
                        Use(keep);
                    }
                    finally
                    {
                        Collect();
                        Churn();
                        result = keep.Value;
                    }
                }
                catch (InvalidOperationException)
                {
                }
                return result;
            }

            // A frame first entered inside the try, which the landing pad pops; the handler publishes nothing, so the
            // method must not leave that frame again.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int PopsInTry(int value)
            {
                if (s_fast) return value;
                var keep = new Holder(value);
                try
                {
                    Use(keep);
                    ThrowNow();
                    Use(keep);
                }
                catch (InvalidOperationException)
                {
                    return value;
                }
                return -1;
            }

            // As PopsInTry, but the handler collects and rethrows: the exception's cell is entered where the popped
            // frame was, so a clear through a frame local still naming that frame would unroot the exception.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void RethrowAfterCollect(int value)
            {
                if (s_fast) return;
                var keep = new Holder(value);
                try
                {
                    Use(keep);
                    ThrowNow();
                    Use(keep);
                }
                catch (InvalidOperationException)
                {
                    Collect();
                    Churn();
                    throw;
                }
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int CatchRethrown(int value)
            {
                try
                {
                    RethrowAfterCollect(value);
                    return -1;
                }
                catch (InvalidOperationException exception)
                {
                    return exception.Message.Length > 0 ? value : -2;
                }
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Touch(int[] values)
            {
                if (values.Length != 8) throw new ArgumentException();
            }

            // An array whose metadata the method caches, which is published at safepoints rather than written where it
            // is stored; the handler reads it after a collection.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ArrayInCatch(int value)
            {
                if (s_fast) return value;
                var keep = new int[8];
                for (var index = 0; index < keep.Length; index++) keep[index] = value + index;
                try
                {
                    Touch(keep);
                    ThrowNow();
                    Touch(keep);
                }
                catch (InvalidOperationException)
                {
                    Collect();
                    Churn();
                    return keep[3] - 3;
                }
                return -1;
            }

            // A root the handler publishes first, alive across a collection after the try.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int AfterHandler(int value)
            {
                if (s_fast) return value;
                Holder? made = null;
                try
                {
                    ThrowNow();
                }
                catch (InvalidOperationException)
                {
                    made = new Holder(value);
                    Use(made);
                }
                Collect();
                Churn();
                return made?.Value ?? -1;
            }

            // Publishes a holder, throws past it and collects in its handler; nothing it published may outlive it.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Leaky(WeakReference[] weak, int value)
            {
                if (s_fast) return;
                var keep = new Holder(value);
                weak[0] = new WeakReference(keep);
                try
                {
                    Use(keep);
                    ThrowNow();
                    Use(keep);
                }
                catch (InvalidOperationException)
                {
                    Collect();
                    Use(keep);
                }
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static async Task<int> ThrowingAsync(WeakReference[] weak, int value)
            {
                if (s_fast) return value;
                var keep = new Holder(value);
                weak[1] = new WeakReference(keep);
                Use(keep);
                ThrowNow();
                Use(keep);
                return keep.Value;
            }

            // Awaited directly, so the callee runs as a direct call.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static async Task<int> AwaitThrowing(WeakReference[] weak, int value)
            {
                try
                {
                    return await ThrowingAsync(weak, value);
                }
                catch (InvalidOperationException)
                {
                    return value;
                }
            }

            public static int Run(int input)
            {
                s_throw = input > 0;
                s_fast = input > 1000;
                if (InCatch(input) != input) return 10;
                if (InFinally(input) != input) return 11;
                if (AfterHandler(input) != input) return 12;
                if (PopsInTry(input) != input) return 17;
                if (ArrayInCatch(input) != input) return 18;
                if (CatchRethrown(input) != input) return 19;
                var weak = new WeakReference[2];
                for (var index = 0; index < 1000; index++) Leaky(weak, index);
                for (var index = 0; index < 1000; index++)
                {
                    if (AwaitThrowing(weak, index).GetAwaiter().GetResult() != index) return 13;
                    var faulted = ThrowingAsync(weak, index);
                    if (!faulted.IsFaulted) return 14;
                }
                Collect();
                Churn();
                if (weak[0].IsAlive) return 15;
                if (weak[1].IsAlive) return 16;
                return input + 1;
            }
        }
        """;

    [Theory]
    [InlineData("Debug-Wasm32-Direct-Linked", FinalWasmOptimization.None)]
    [InlineData("Release-Wasm32-Direct-Linked", FinalWasmOptimization.None)]
    [InlineData("Release-Wasm32-Direct-Linked", FinalWasmOptimization.O3)]
    [InlineData("Release-Wasm32-Direct-Linked", FinalWasmOptimization.Oz)]
    [InlineData("Debug-Wasm64-Direct-Linked", FinalWasmOptimization.None)]
    [InlineData("Release-Wasm64-Direct-Linked", FinalWasmOptimization.O3)]
    public void ALazyRootFrameHoldsWhatHandlersUseAndLeavesNothingBehind(string cell, FinalWasmOptimization optimization)
    {
        var fixture = new CorpusFixture("LazyRootFrameFixture", "LazyRootFrameFixture", Source, [41])
        {
            CaseId = "frame.lazy-root-frame",
            ExpectedReturnValue = 42,
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection,
            Matrix = new(CorpusMatrixProfile.Extended, cell, CorpusExecutionBackend.Linked),
            ExposesLegacyTrace = false,
            OracleMode = OracleMode.SameSource,
            SameSourceReason = "Compare roots held across protected blocks by lazily entered root frames, and what throwing calls leave behind, against the desktop runtime.",
            Optimization = optimization,
        };
        runner.Run(fixture);
    }
}
