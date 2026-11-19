// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

// A collector with generations is not told of a store into an object the method allocated with no safepoint
// since, nor of a constructor's stores into a this every caller passes young. Each shape here stores a young object
// into one that a collection may have made old just before: after a constructor that collects, after an allocation
// for the value, in a base constructor chained to after a collection, after a call in a constructor, in a loop
// that collects, and in a handler; and, alongside, into objects that are young (an array initializer, an object
// initializer, a constructor only newobj reaches, a constructor chained to first). Each risky store takes the
// object from a static it then clears, so that the holder alone keeps it, and every object is read back after more
// collections. On NetWasmGen (NETWASM_CORPUS_COLLECTOR=netwasmgen) a store that should have been recorded and
// was not frees the object it stored, and with a collection before every allocation (NETWASM_CORPUS_GC_STRESS=1) the
// runtime names the store.
public sealed class YoungObjectStoreGcTests(CorrectnessTestRunner runner)
{
    [Theory]
    [InlineData("Release-Wasm32-Direct-Linked")]
    [InlineData("Release-Wasm64-Direct-Linked")]
    [InlineData("Release-Wasm32-Optimized-Linked")]
    public void StoresIntoObjectsThatMayBeOldKeepWhatTheyStore(string cell) =>
        runner.Run(new("YoungObjectStore", "YoungObjectStore", Source, [0, 5])
        {
            ExpectedReturnValue = 42,
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection,
            Matrix = new(CorpusMatrixProfile.Extended, cell, CorpusExecutionBackend.Linked),
        });

    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Runtime.CompilerServices;
        namespace YoungObjectStore;

        public sealed class Node
        {
            public Node? Next;
            public int Id;
        }

        // Only newobj reaches it, and it stores before anything collects.
        public sealed class Pair
        {
            public Node? First;
            public Node? Second;

            public Pair(Node first, Node second)
            {
                First = first;
                Second = second;
            }
        }

        // Collects, then stores the young object the collection left.
        public sealed class Late
        {
            public Node? Value;

            public Late(int id)
            {
                EntryPoint.Refresh(id);
                Value = EntryPoint.Young;
                EntryPoint.Young = null;
            }
        }

        // Its constructor collects and leaves a young object behind.
        public sealed class Collecting
        {
            public Node? Value;

            public Collecting(int id) => EntryPoint.Refresh(id);
        }

        // Kept out of line, so that its store stays in its own body, where only what reaches it decides.
        public class Base
        {
            public Node? Value;

            [MethodImpl(MethodImplOptions.NoInlining)]
            public Base(Node value) => Value = value;
        }

        // Its field initializer collects before the base constructor stores.
        public sealed class Derived : Base
        {
            public int FillerId = EntryPoint.Refresh(EntryPoint.NextId++).Id;

            public Derived() : base(EntryPoint.Take())
            {
            }
        }

        // Chains to its base first, with a young this.
        public sealed class Chained : Base
        {
            public Chained(Node value) : base(value)
            {
            }
        }

        // Stores at the top of a loop that collects, and in a handler.
        public sealed class Looping
        {
            public Node? Value;
            public Node? Caught;

            public Looping(int id)
            {
                for (var round = 0; round < 3; round++)
                {
                    Value = EntryPoint.Young;
                    EntryPoint.Young = null;
                    EntryPoint.Refresh(id + round);
                }
                try
                {
                    EntryPoint.Fail(id + 10);
                }
                catch (InvalidOperationException)
                {
                    Caught = EntryPoint.Young;
                    EntryPoint.Young = null;
                }
            }
        }

        public static class EntryPoint
        {
            public static Node? Young;
            public static int NextId = 900000;
            public static int Trace() => 0;

            public static int Run(int seed)
            {
                var kept = new List<object>();
                var expected = new List<int>();
                GC.Collect();
                for (var round = 0; round < 3; round++)
                {
                    var id = seed + 1000 * round;
                    Young = new Node { Id = id + 1 };

                    // Young holders.
                    var array = new object?[] { Young, null };
                    var initialized = new Node { Next = Young, Id = id + 2 };
                    var pair = new Pair(Young, Young);
                    var chained = new Chained(Young);
                    kept.Add(array); kept.Add(initialized); kept.Add(pair); kept.Add(chained);

                    // Holders a collection may have made old before the store.
                    var late = new Late(id + 3);
                    var collecting = MakeCollecting(id + 4);
                    var holder = MakeHolder(id + 5);
                    var derived = new Derived();
                    var looping = new Looping(id + 20);
                    kept.Add(late); kept.Add(collecting); kept.Add(holder); kept.Add(derived); kept.Add(looping);
                    expected.Add(id);

                    Refresh(id + 50);
                    Refresh(id + 51);
                    if (Check(kept, expected) is var failure and not 0) return failure + round;
                }
                return 42;
            }

            private static int Check(List<object> kept, List<int> expected)
            {
                for (var round = 0; round < expected.Count; round++)
                {
                    var id = expected[round];
                    var at = round * 9;
                    if (((Node)((object?[])kept[at])[0]!).Id != id + 1) return 100;
                    if (((Node)kept[at + 1]).Next!.Id != id + 1) return 110;
                    var pair = (Pair)kept[at + 2];
                    if (pair.First!.Id != id + 1 || pair.Second!.Id != id + 1) return 120;
                    if (((Chained)kept[at + 3]).Value!.Id != id + 1) return 130;
                    if (((Late)kept[at + 4]).Value!.Id != id + 3) return 140;
                    if (((Collecting)kept[at + 5]).Value!.Id != id + 4) return 150;
                    if (((Node)kept[at + 6]).Next!.Id != id + 6) return 160;
                    var derived = (Derived)kept[at + 7];
                    if (derived.Value!.Id != derived.FillerId) return 170;
                    var looping = (Looping)kept[at + 8];
                    if (looping.Value!.Id != id + 21 || looping.Caught!.Id != id + 30) return 180;
                }
                return 0;
            }

            // Collects, then leaves a new, young object in Young and returns it.
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static Node Refresh(int id)
            {
                Churn();
                return Young = new Node { Id = id };
            }

            // Its frame, and any stale root in it, is gone before the next collection, so the holder alone keeps
            // what it stored.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static Collecting MakeCollecting(int id)
            {
                var collecting = new Collecting(id);
                collecting.Value = Young;
                Young = null;
                return collecting;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static Node MakeHolder(int id)
            {
                var holder = new Node { Id = id };
                holder.Next = Refresh(id + 1);
                Young = null;
                return holder;
            }

            // Young, which it clears.
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static Node Take()
            {
                var young = Young!;
                Young = null;
                return young;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static void Fail(int id)
            {
                Refresh(id);
                throw new InvalidOperationException();
            }

            private static void Churn()
            {
                var before = GC.CollectionCount(0);
                for (var index = 0; GC.CollectionCount(0) == before; index++) Fill(index);
                for (var index = 0; index < 768; index++) Fill(index);
            }

            private static void Fill(int index)
            {
                var filler = new int[1 + index % 24];
                Array.Fill(filler, -1);
            }
        }
        """;
}
