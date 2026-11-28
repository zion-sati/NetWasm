// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Tests.Correctness;

namespace NetWasm.Compiler.Tests;

// A method whose only points that collect are allocations enters its root frame only where an allocation falls back to
// the collector, so a reference live across its allocations (an earlier object, an argument, a string) is published
// into a frame entered there. Each must read back as built after the collections the fallbacks make, across thousands
// of calls that fill the allocator's runs, with forced collections and garbage that reuses freed memory between them.
public sealed class AllocationOnlyRootFrameGcTests(CorrectnessTestRunner runner)
{
    internal const string Source = """
        using System;
        using System.Runtime.CompilerServices;

        namespace AllocationOnlyRootFrameFixture;

        public sealed class Holder
        {
            public int Value;

            public Holder(int value) => Value = value;
        }

        public sealed class Node
        {
            public Holder First;
            public Holder Second;
            public string Text;

            public Node(Holder first, Holder second, string text)
            {
                First = first;
                Second = second;
                Text = text;
            }
        }

        public sealed class Box
        {
            public readonly string Text;

            public Box(string text) => Text = text;
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

            // Each holder stays live across the next allocation.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static Node Build(int value)
            {
                var first = new Holder(value);
                var second = new Holder(value + 1);
                return new Node(first, second, "built");
            }

            // An argument live across the method's allocations.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static Node Wrap(Holder held, int value)
            {
                var extra = new Holder(value);
                return new Node(held, extra, "wrapped");
            }

            // A string argument kept across the one allocation, as Task.FromResult keeps its result.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static Box Hold(string text) => new Box(text);

            public static int Run(int input)
            {
                var nodes = new Node[4096];
                var boxes = new Box[4096];
                for (var index = 0; index < nodes.Length; index++)
                {
                    nodes[index] = (index & 1) == 0 ? Build(index) : Wrap(new Holder(index), index + 1);
                    boxes[index] = Hold(index.ToString());
                    if ((index & 255) == 0)
                    {
                        Collect();
                        Churn();
                    }
                }
                Collect();
                Churn();
                for (var index = 0; index < nodes.Length; index++)
                {
                    var node = nodes[index];
                    if (node.First.Value != index || node.Second.Value != index + 1) return 10;
                    if (node.Text != ((index & 1) == 0 ? "built" : "wrapped")) return 11;
                    if (boxes[index].Text != index.ToString()) return 12;
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
    public void ReferencesLiveAcrossAllocationsReadBackAsBuilt(string cell, FinalWasmOptimization optimization)
    {
        var fixture = new CorpusFixture("AllocationOnlyRootFrameFixture", "AllocationOnlyRootFrameFixture", Source, [41])
        {
            CaseId = "frame.allocation-only-root-frame",
            ExpectedReturnValue = 42,
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection,
            Matrix = new(CorpusMatrixProfile.Extended, cell, CorpusExecutionBackend.Linked),
            ExposesLegacyTrace = false,
            OracleMode = OracleMode.SameSource,
            SameSourceReason = "Compare references held across allocations, in methods whose only collecting points are allocations, against the desktop runtime.",
            Optimization = optimization,
        };
        runner.Run(fixture);
    }
}
