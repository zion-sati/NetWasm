// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

// GC.Collect(0) collects the youngest generation. With generations (desktop .NET, and NetWasmGen through
// NETWASM_CORPUS_COLLECTOR=netwasmgen) a young object a local holds survives it and is then in generation 1, it counts
// a collection of generation 0 and none of generation 1, and an old object the program dropped survives it, so a weak
// reference to it stays alive until a collection of every object. Without generations it collects every object, as
// GC.Collect() does. The program reads GC.MaxGeneration to know which holds, and returns 42 when every check does.
public sealed class YoungCollectionGcTests(CorrectnessTestRunner runner)
{
    [Theory]
    [InlineData("Release-Wasm32-Direct-Linked")]
    [InlineData("Release-Wasm64-Direct-Linked")]
    [InlineData("Release-Wasm32-Optimized-Linked")]
    public void CollectingGenerationZeroFreesOnlyYoungObjects(string cell) =>
        runner.Run(new("YoungCollection", "YoungCollection", Source, [0, 1, 2])
        {
            ExpectedReturnValue = 42,
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection,
            Matrix = new(CorpusMatrixProfile.Extended, cell, CorpusExecutionBackend.Linked),
        });

    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;
        namespace YoungCollection;

        public sealed class Node
        {
            public Node? Next;
            public int Id;
        }

        public static class EntryPoint
        {
            public static Node? Kept;
            public static int Trace() => 0;

            public static int Run(int input) => input switch
            {
                0 => YoungObjectsALocalHoldsSurvive(),
                1 => OnlyGenerationZeroIsCounted(),
                2 => DroppedOldObjectsSurviveUntilAFullCollection(),
                _ => -1,
            };

            private static bool HasGenerations => GC.MaxGeneration > 0;

            private static int YoungObjectsALocalHoldsSurvive()
            {
                GC.Collect();
                var young = MakeYoung(5);
                GC.Collect(0);
                if (GC.GetGeneration(young) != (HasGenerations ? 1 : 0)) return 10;
                if (young.Id != 5 || young.Next!.Id != 6) return 11;
                // Young again, under one that is now old.
                young.Next = MakeYoung(7);
                GC.Collect(0);
                GC.Collect(0);
                return young.Next.Id == 7 && young.Next.Next!.Id == 8 ? 42 : 12;
            }

            private static int OnlyGenerationZeroIsCounted()
            {
                GC.Collect();
                var young = GC.CollectionCount(0);
                var full = GC.CollectionCount(1);
                GC.Collect(0);
                var youngAfter = GC.CollectionCount(0);
                var fullAfter = GC.CollectionCount(1);
                if (youngAfter <= young) return 20;
                if (HasGenerations ? fullAfter != full : fullAfter <= full) return 21;
                GC.Collect();
                return GC.CollectionCount(1) > fullAfter ? 42 : 22;
            }

            private static int DroppedOldObjectsSurviveUntilAFullCollection()
            {
                var weak = KeepAndWatch(9);
                GC.Collect();
                if (!weak.IsAlive) return 30;
                Kept = null;
                GC.Collect(0);
                if (weak.IsAlive != HasGenerations) return 31;
                GC.Collect();
                return weak.IsAlive ? 32 : 42;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static Node MakeYoung(int id) => new() { Id = id, Next = new Node { Id = id + 1 } };

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static WeakReference KeepAndWatch(int id)
            {
                Kept = MakeYoung(id);
                return new WeakReference(Kept);
            }
        }
        """;
}
