// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Tests.Correctness;

namespace NetWasm.Compiler.Tests;

// A method that builds a value type in a local (an inlined constructor or an object initializer) and returns it keeps
// the local in its caller's return buffer, with no frame of its own, where nothing collects while the value is built.
// So each returned value must read back as built: its references, scalars and the fields left default, across
// collections in the caller, a value returned through ValueTask, one built after a call that collects (kept in a frame)
// and a method with two returns. Each collection is followed by garbage that reuses freed memory.
public sealed class ReturnBufferLocalGcTests(CorrectnessTestRunner runner)
{
    internal const string Source = """
        using System;
        using System.Runtime.CompilerServices;
        using System.Threading.Tasks;

        namespace ReturnBufferLocalFixture;

        public sealed class Holder
        {
            public int Value;

            public Holder(int value) => Value = value;
        }

        public struct Pair
        {
            public Holder? First;
            public int Count;
            public Holder? Second;
            public short Tag;
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

            // Built in the caller's buffer: an object initializer, the second reference and the tag left default.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static Pair MakeFirst(Holder first, int count) => new Pair { First = first, Count = count };

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static Pair MakeBoth(Holder first, Holder second, int count) =>
                new Pair { First = first, Count = count, Second = second, Tag = 7 };

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static ValueTask<Holder> MakeTask(Holder holder) => new ValueTask<Holder>(holder);

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int CollectAndCount(int count)
            {
                Collect();
                Churn();
                return count;
            }

            // A call that collects before the value is built: the local stays in a frame.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static Pair MakeAfterCall(Holder first, int count)
            {
                var counted = CollectAndCount(count);
                return new Pair { First = first, Count = counted };
            }

            // Two returns.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static Pair MakeEither(Holder holder, bool first) =>
                first ? new Pair { First = holder, Count = 1 } : new Pair { Second = holder, Count = 2 };

            public static int Run(int input)
            {
                var pairs = new Pair[64];
                var tasks = new ValueTask<Holder>[64];
                for (var index = 0; index < pairs.Length; index++)
                {
                    pairs[index] = (index & 3) switch
                    {
                        0 => MakeFirst(new Holder(index), index),
                        1 => MakeBoth(new Holder(index), new Holder(index + 1000), index),
                        2 => MakeAfterCall(new Holder(index), index),
                        _ => MakeEither(new Holder(index), (index & 4) == 0),
                    };
                    tasks[index] = MakeTask(new Holder(index + 2000));
                    if ((index & 7) == 0)
                    {
                        Collect();
                        Churn();
                    }
                }
                Collect();
                Churn();
                for (var index = 0; index < pairs.Length; index++)
                {
                    var pair = pairs[index];
                    var held = pair.First ?? pair.Second;
                    if (held is null || held.Value != index) return 10;
                    switch (index & 3)
                    {
                        case 0:
                            if (pair.Second is not null || pair.Tag != 0 || pair.Count != index) return 11;
                            break;
                        case 1:
                            if (pair.Second is null || pair.Second.Value != index + 1000 || pair.Tag != 7) return 12;
                            break;
                        case 2:
                            if (pair.Second is not null || pair.Count != index) return 13;
                            break;
                        default:
                            if (pair.Count != ((index & 4) == 0 ? 1 : 2)) return 14;
                            break;
                    }
                    if (tasks[index].Result.Value != index + 2000) return 15;
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
    public void AValueBuiltInTheReturnBufferReadsBackAsBuilt(string cell, FinalWasmOptimization optimization)
    {
        var fixture = new CorpusFixture("ReturnBufferLocalFixture", "ReturnBufferLocalFixture", Source, [41])
        {
            CaseId = "frame.return-buffer-local",
            ExpectedReturnValue = 42,
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection,
            Matrix = new(CorpusMatrixProfile.Extended, cell, CorpusExecutionBackend.Linked),
            ExposesLegacyTrace = false,
            OracleMode = OracleMode.SameSource,
            SameSourceReason = "Compare value types built in the caller's return buffer, across collections, against the desktop runtime.",
            Optimization = optimization,
        };
        runner.Run(fixture);
    }
}
