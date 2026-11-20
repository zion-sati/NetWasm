// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Tests.Correctness;

namespace NetWasm.Compiler.Tests;

// A method whose safepoints would otherwise clear many dead root slots zeroes its root frame on entry instead, and
// then stores nothing into a slot it has not written. The frame reuses arena space an earlier call's frame used, so
// the zeroing is what keeps that call's references from looking alive: a collection at the first safepoint, where
// every local's slot is dead, must not keep them alive.
public sealed class RootFrameZeroingCompilationTests(CorrectnessTestRunner runner)
{
    internal const string Source = """
        using System;
        using System.Runtime.CompilerServices;

        namespace RootFrameZeroingFixture;

        public static class EntryPoint
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Collect()
            {
                GC.Collect();
                GC.Collect();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Touch() { }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Length(int[] value) => value.Length;

            // Holds eight references in its root frame across a call, and returns weak references to them.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static WeakReference[] Leave()
            {
                int[] a = new int[1], b = new int[2], c = new int[3], d = new int[4];
                int[] e = new int[5], f = new int[6], g = new int[7], h = new int[8];
                Touch();
                var weak = new WeakReference[] { new(a), new(b), new(c), new(d), new(e), new(f), new(g), new(h) };
                return Length(a) + Length(b) + Length(c) + Length(d) + Length(e) + Length(f) + Length(g) + Length(h) == 36
                    ? weak : [];
            }

            // Its frame takes the place of Leave's. At the collection only the argument's slot is live; the
            // locals' slots, written after it, must hold null, not what Leave's frame left there.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static bool StaleReferencesSurvive(WeakReference[] weak)
            {
                Collect();
                var alive = false;
                foreach (var reference in weak) alive |= reference.IsAlive;
                int[] a = new int[1], b = new int[2], c = new int[3], d = new int[4];
                int[] e = new int[5], f = new int[6], g = new int[7], h = new int[8];
                Touch();
                return alive ||
                    Length(a) + Length(b) + Length(c) + Length(d) + Length(e) + Length(f) + Length(g) + Length(h) != 36;
            }

            public static int Run(int input)
            {
                var weak = Leave();
                if (weak.Length != 8) return 6;
                return StaleReferencesSurvive(weak) ? 7 : 42;
            }
        }
        """;

    [Theory]
    [InlineData("Debug-Wasm32-Direct-Linked")]
    [InlineData("Release-Wasm32-Direct-Linked")]
    [InlineData("Debug-Wasm64-Direct-Linked")]
    [InlineData("Release-Wasm64-Direct-Linked")]
    public void AZeroedRootFrameHoldsNothingAnEarlierFrameLeft(string cell)
    {
        var fixture = new CorpusFixture("RootFrameZeroingFixture", "RootFrameZeroingFixture", Source, [41])
        {
            CaseId = "frame.root-frame-zeroing",
            ExpectedReturnValue = 42,
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection,
            Matrix = new(CorpusMatrixProfile.Extended, cell, CorpusExecutionBackend.Linked),
            ExposesLegacyTrace = false,
            OracleMode = OracleMode.SameSource,
            SameSourceReason = "Compare a collection over a root frame that reuses an earlier frame's arena space against the desktop runtime.",
            Optimization = FinalWasmOptimization.None,
        };
        runner.Run(fixture);
    }
}
