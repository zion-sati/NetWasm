// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Tests.Correctness;

namespace NetWasm.Compiler.Tests;

// A value built on the evaluation stack around a fresh object, passed to a compiled callee that allocates before it reads
// the object through its parameter (which it neither writes nor addresses), or that passes the value on to another such
// callee: the caller no longer roots the references inside an argument the callee takes, so the callee must keep them,
// as it keeps an object reference it is passed. A value still on the stack below another call that collects (an argument
// the call does not take) stays the caller's to root. Each read must see the object as built after the collections the
// allocations make, across thousands of calls, with forced collections and garbage that reuses freed memory.
public sealed class ValueArgumentEmbeddedRootGcTests(CorrectnessTestRunner runner)
{
    internal const string Source = """
        using System;
        using System.Runtime.CompilerServices;

        namespace ValueArgumentEmbeddedRootFixture;

        public sealed class Holder
        {
            public int Value;

            public Holder(int value) => Value = value;
        }

        public readonly struct Pair
        {
            public readonly Holder First;
            public readonly Holder Second;

            public Pair(Holder first, Holder second)
            {
                First = first;
                Second = second;
            }
        }

        public static class EntryPoint
        {
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

            // Allocates, collecting, before it reads what its parameter holds.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Read(Pair pair)
            {
                Churn();
                if ((pair.First.Value & 63) == 0) Collect();
                return pair.First.Value * 3 + pair.Second.Value;
            }

            // Allocates and collects while the caller's value is below its call on the stack.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static Holder Fresh(int value)
            {
                Churn();
                if ((value & 63) == 1) Collect();
                return new Holder(value);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Combine(Pair pair, Holder extra) =>
                pair.First.Value * 3 + pair.Second.Value + extra.Value - extra.Value;

            // Passes its parameter on, unread, after allocating.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Forward(Pair pair, int value)
            {
                s_sink = new Holder(value);
                return Read(pair);
            }

            public static int Run(int input)
            {
                var total = 0L;
                var expected = 0L;
                for (var index = 0; index < 4096; index++)
                {
                    // Each pair is built on the stack as the call's argument: no local holds it.
                    total += (index % 3) switch
                    {
                        0 => Read(new Pair(new Holder(index), new Holder(index + 1))),
                        1 => Forward(new Pair(new Holder(index), new Holder(index + 1)), index),
                        // The pair is on the stack while Fresh allocates: it is not Fresh's argument.
                        _ => Combine(new Pair(new Holder(index), new Holder(index + 1)), Fresh(index)),
                    };
                    expected += index * 3L + index + 1;
                }
                return total == expected ? input + 1 : 10;
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
    public void ReferencesInsideAValueArgumentReadBackAsBuilt(string cell, FinalWasmOptimization optimization)
    {
        var fixture = new CorpusFixture("ValueArgumentEmbeddedRootFixture", "ValueArgumentEmbeddedRootFixture", Source, [41])
        {
            CaseId = "frame.value-argument-embedded-root",
            ExpectedReturnValue = 42,
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection,
            Matrix = new(CorpusMatrixProfile.Extended, cell, CorpusExecutionBackend.Linked),
            ExposesLegacyTrace = false,
            OracleMode = OracleMode.SameSource,
            SameSourceReason = "Compare references inside value arguments built on the stack, read by callees after they allocate, against the desktop runtime.",
            Optimization = optimization,
        };
        runner.Run(fixture);
    }
}
