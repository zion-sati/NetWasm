// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Tests.Correctness;

namespace NetWasm.Compiler.Tests;

// A method that holds no frame of its own keeps only the frame arenas' cursors for its landing pad, and the runtime finds
// from the value arena's cursor which of the frames on the value list the exception left: those in a later chunk, or at
// or above the cursor in its own. A collection in the catch must then drop what only the thrower's frames held, and keep
// what a frame below the cursor holds: Outer's struct, whose address it takes, lives in a mapped frame on the list under
// the catcher's mark. A thrower one call down leaves its frames in the cursor's chunk; one 400 calls down crosses into
// later chunks (4 KiB each), and throwing from there again reuses them.
public sealed class ValueCursorRewindGcTests(CorrectnessTestRunner runner)
{
    internal const string Source = """
        using System;
        using System.Runtime.CompilerServices;

        namespace ValueCursorRewindFixture;

        public sealed class Box
        {
            public int Number;

            public Box(int number)
            {
                Number = number;
            }
        }

        public struct Holder
        {
            public object Value;
        }

        public static class EntryPoint
        {
            private static bool s_throw;
            private static WeakReference? s_deepest;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Collect()
            {
                GC.Collect();
                GC.Collect();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Fill(ref Holder holder, int number) => holder.Value = new Box(number);

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Keep(object value) { }

            // Every level keeps a struct holding a reference in its value frame (it passes the struct's address), and
            // the deepest throws, when the input says so.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Deep(int depth)
            {
                var holder = new Holder();
                Fill(ref holder, depth);
                if (depth == 0)
                {
                    s_deepest = new WeakReference(holder.Value);
                    if (s_throw) throw new InvalidOperationException();
                    return 0;
                }
                var result = Deep(depth - 1) + 1;
                Keep(holder.Value);
                return result;
            }

            // Holds no reference across its protected call and passes no address, so it has no frame of its own.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Catcher(int depth)
            {
                try
                {
                    return Deep(depth);
                }
                catch (InvalidOperationException)
                {
                    Collect();
                    return s_deepest!.IsAlive ? -2 : -1;
                }
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Outer(int depth)
            {
                var mine = new Holder();
                Fill(ref mine, 1000 + depth);
                var caught = Catcher(depth);
                Collect();
                // Allocations that would take the place of Outer's box had the collection dropped it.
                for (var index = 0; index < 1000; index++) Keep(new Box(index));
                return mine.Value is Box box && box.Number == 1000 + depth ? caught : -100;
            }

            public static int Run(int input)
            {
                s_throw = input > 0;
                var depths = new[] { 1, 400, 400 };
                for (var index = 0; index < depths.Length; index++)
                {
                    var result = Outer(depths[index]);
                    if (result != (s_throw ? -1 : depths[index])) return 100 * (index + 1) - result;
                }
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
    public void ACatchInAFramelessMethodDropsTheThrowersFramesAndKeepsItsCallers(string cell, FinalWasmOptimization optimization)
    {
        var fixture = new CorpusFixture("ValueCursorRewindFixture", "ValueCursorRewindFixture", Source, [41])
        {
            CaseId = "frame.value-cursor-rewind",
            ExpectedReturnValue = 42,
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection,
            Matrix = new(CorpusMatrixProfile.Extended, cell, CorpusExecutionBackend.Linked),
            ExposesLegacyTrace = false,
            OracleMode = OracleMode.SameSource,
            SameSourceReason = "Compare a collection in a catch, after an exception from one and from 400 calls down, against the desktop runtime.",
            Optimization = optimization,
        };
        runner.Run(fixture);
    }
}
