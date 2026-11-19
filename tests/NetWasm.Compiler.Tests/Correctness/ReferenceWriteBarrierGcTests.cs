// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

// A collector with generations finds an old object's young ones only through the stores the program records,
// so every way of storing a reference into an object needs its write barrier. This program makes its holders
// old with a collection, stores a newly allocated object into them in each of those ways, lets the collector
// run, and reads every one back. On a collector without generations it holds trivially; on NetWasmGen
// (NETWASM_CORPUS_COLLECTOR=netwasmgen) a store without its barrier frees the object, whose memory the
// allocations that follow overwrite, and with a collection before every allocation
// (NETWASM_CORPUS_GC_STRESS=1) the runtime names the store.
public sealed class ReferenceWriteBarrierGcTests(CorrectnessTestRunner runner)
{
    [Theory]
    [InlineData("Release-Wasm32-Direct-Linked")]
    [InlineData("Release-Wasm64-Direct-Linked")]
    [InlineData("Release-Wasm32-Optimized-Linked")]
    public void EveryStoreOfAReferenceIntoAnOldObjectKeepsTheStoredObject(string cell) =>
        runner.Run(new("ReferenceWriteBarrier", "ReferenceWriteBarrier", Source, [0, 5])
        {
            ExpectedReturnValue = 42,
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection,
            Matrix = new(CorpusMatrixProfile.Extended, cell, CorpusExecutionBackend.Linked),
        });

    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Linq.Expressions;
        using System.Reflection;
        using System.Runtime.CompilerServices;
        using System.Threading;
        namespace ReferenceWriteBarrier;

        public sealed class Node
        {
            public Node? Next;
            public object? Slot;
            public Pair Pair;
            public Func<int>? Read;
            public int Id;
        }

        public struct Pair
        {
            public object? First;
            public int Count;
            public Node? Second;
        }

        public delegate int Twice(int value);

        public static class EntryPoint
        {
            private const int Length = 8192;
            private const int YoungLength = 300;
            private static Node s_root = null!;
            private static int[]? s_filler;
            public static int Trace() => 0;

            public static int Run(int seed)
            {
                // The holders, some of them large objects, made old by a collection of every object.
                var node = new Node { Id = -1 };
                s_root = node;
                var nodes = new Node?[Length];
                var pairs = new Pair[Length];
                var objects = new object?[Length];
                var jagged = new Node?[64][];
                var grid = new Node?[32, 32];
                var list = new List<Node>(Length);
                var map = new Dictionary<int, Node>();
                var arguments = new object?[] { null };
                for (var index = 0; index < 64; index++)
                {
                    list.Add(new Node { Id = -2 });
                    map[index] = new Node { Id = -3 };
                }
                GC.Collect();

                for (var round = 0; round < 3; round++)
                {
                    var id = seed + 100 * round;

                    // A field, and a field of a value in an object: by the value and through its address.
                    node.Next = Fresh(id + 1);
                    node.Pair = new Pair { First = Fresh(id + 2), Count = id, Second = Fresh(id + 3) };
                    node.Pair.First = Fresh(id + 4);
                    s_root.Slot = Fresh(id + 5);

                    // Elements of a large array, far apart, of references and of values that hold them.
                    nodes[7] = Fresh(id + 6);
                    nodes[Length / 2 + 1] = Fresh(id + 7);
                    nodes[Length - 1] = Fresh(id + 8);
                    objects[4000] = Fresh(id + 9);
                    pairs[6000] = new Pair { First = Fresh(id + 10), Second = Fresh(id + 11) };
                    pairs[6001].Second = Fresh(id + 12);
                    grid[3, 30] = Fresh(id + 13);
                    jagged[40] = new[] { Fresh(id + 14), Fresh(id + 15) };

                    // Through a ref: a reference, a value that holds one, and a generic of either.
                    Set(ref nodes[5000], Fresh(id + 16));
                    Set(ref pairs[5001], new Pair { First = Fresh(id + 17) });
                    SetNode(ref node.Pair.Second, Fresh(id + 18));

                    // Exchanges and volatile stores.
                    var replaced = node.Read;
                    Interlocked.CompareExchange(ref nodes[100], Fresh(id + 19), nodes[100]);
                    Interlocked.Exchange(ref nodes[101], Fresh(id + 20));
                    Volatile.Write(ref nodes[102], Fresh(id + 21));

                    // Whole ranges: a fill, a span copy and an array copy from a young array, and new objects moved
                    // along the old array itself and then cleared where they were, so the moved elements are all
                    // that keeps them.
                    nodes.AsSpan(200, 40).Fill(Fresh(id + 22));
                    SpanCopyFromYoung(nodes, id);
                    ArrayCopyFromYoung(nodes, id);
                    MoveAlong(nodes, id);
                    ((Array)objects).SetValue(Fresh(id + 23), 4001);

                    // Collections: a list's array, grown and shifted, and a dictionary's entries.
                    list.Add(Fresh(id + 24));
                    list.Insert(0, Fresh(id + 25));
                    list[10] = Fresh(id + 26);
                    map[1000 + round] = Fresh(id + 27);
                    map[3] = Fresh(id + 28);

                    // A box, a delegate over a young object, a combined delegate and a string.
                    objects[4002] = id + 29;
                    var captured = Fresh(id + 30);
                    node.Read = () => captured.Id;
                    Twice twice = value => value * 2;
                    twice += value => value * 2 + captured.Id;
                    objects[4003] = twice;
                    objects[4004] = "id" + (id + 31);

                    // A store inside an exception filter, which is compiled as a function of its own.
                    try
                    {
                        throw new InvalidOperationException();
                    }
                    catch (InvalidOperationException) when ((objects[4005] = Fresh(id + 32)) is not null)
                    {
                    }

                    // DynamicInvoke stores the value a null argument stands for into the caller's array.
                    arguments[0] = null;
                    Twice plain = value => value + 7;
                    if ((int)plain.DynamicInvoke(arguments)! != 7) return 1;

                    // A struct holding references returned into a box allocated before the call, which the call's
                    // own allocations may make old: through DynamicInvoke and through an expression's call.
                    Func<int, Pair> maker = MakePair;
                    var invoked = maker.DynamicInvoke(id + 33)!;
                    Expression<Func<Pair>> call = () => MakePair(id + 35);
                    object called = call.Compile()();

                    // An expression's assignment of a field of an old object.
                    Expression<Func<Node, object?>> slot = holder => holder.Slot;
                    var field = (FieldInfo)((MemberExpression)slot.Body).Member;
                    var target = Expression.Parameter(typeof(Node));
                    var value = Expression.Parameter(typeof(object));
                    var assign = Expression.Lambda<Action<Node, object>>(
                        Expression.Assign(Expression.Field(target, field), value), target, value).Compile();
                    assign(map[5], Fresh(id + 37));

                    Churn();

                    if ((((Pair)invoked).First as Node)?.Id != id + 33 || ((Pair)invoked).Second?.Id != id + 34)
                        return 250 + round;
                    if ((((Pair)called).First as Node)?.Id != id + 35 || ((Pair)called).Second?.Id != id + 36)
                        return 260 + round;
                    if ((map[5].Slot as Node)?.Id != id + 37) return 270 + round;
                    if (node.Next?.Id != id + 1) return 10 + round;
                    if ((node.Pair.First as Node)?.Id != id + 4 || node.Pair.Count != id) return 20 + round;
                    if (node.Pair.Second?.Id != id + 18) return 30 + round;
                    if ((s_root.Slot as Node)?.Id != id + 5) return 40 + round;
                    if (nodes[7]?.Id != id + 6 || nodes[Length / 2 + 1]?.Id != id + 7 || nodes[Length - 1]?.Id != id + 8)
                        return 50 + round;
                    if ((objects[4000] as Node)?.Id != id + 9) return 60 + round;
                    if ((pairs[6000].First as Node)?.Id != id + 10 || pairs[6000].Second?.Id != id + 11) return 70 + round;
                    if (pairs[6001].Second?.Id != id + 12) return 80 + round;
                    if (grid[3, 30]?.Id != id + 13) return 90 + round;
                    if (jagged[40]?[0]?.Id != id + 14 || jagged[40]?[1]?.Id != id + 15) return 100 + round;
                    if (nodes[5000]?.Id != id + 16 || (pairs[5001].First as Node)?.Id != id + 17) return 110 + round;
                    if (nodes[100]?.Id != id + 19 || nodes[101]?.Id != id + 20 || nodes[102]?.Id != id + 21)
                        return 120 + round;
                    for (var index = 200; index < 240; index++)
                        if (nodes[index]?.Id != id + 22) return 130 + round;
                    for (var index = 0; index < YoungLength; index++)
                        if (nodes[1000 + index]?.Id != id + 1000 + index || nodes[2000 + index]?.Id != id + 2000 + index)
                            return 140 + round;
                    for (var index = 0; index < 3; index++)
                        if (nodes[3000 + index]?.Id != id + 3000 + index || nodes[3100 + index]?.Id != id + 3100 + index)
                            return 145 + round;
                    if (nodes[0] is not null || nodes[3] is not null) return 148 + round;
                    if ((objects[4001] as Node)?.Id != id + 23) return 150 + round;
                    if (list[^1].Id != id + 24 || list[0].Id != id + 25 || list[10].Id != id + 26) return 160 + round;
                    if (map[1000 + round].Id != id + 27 || map[3].Id != id + 28) return 170 + round;
                    if ((int)objects[4002]! != id + 29) return 180 + round;
                    if (node.Read() != id + 30) return 190 + round;
                    if (((Twice)objects[4003]!)(1) != 2 + id + 30) return 200 + round;
                    if ((string)objects[4004]! != "id" + (id + 31)) return 210 + round;
                    if (arguments[0] is int written && written != 0) return 220 + round;
                    if ((objects[4005] as Node)?.Id != id + 32) return 240 + round;
                    if (replaced is not null && replaced() != id - 100 + 30) return 230 + round;
                }
                return 42;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static Node Fresh(int id) => new Node { Id = id };

            // The source is dropped once copied. Where the collector runs before every allocation, everything in
            // it is old by then and the source keeps its elements until the next collection of every object; where
            // it runs now and then, the source and its elements are young and only the copies keep them.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void SpanCopyFromYoung(Node?[] nodes, int id)
            {
                var young = new Node?[YoungLength];
                for (var index = 0; index < young.Length; index++) young[index] = Fresh(id + 1000 + index);
                young.AsSpan().CopyTo(nodes.AsSpan(1000));
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void ArrayCopyFromYoung(Node?[] nodes, int id)
            {
                var young = new Node?[YoungLength];
                for (var index = 0; index < young.Length; index++) young[index] = Fresh(id + 2000 + index);
                Array.Copy(young, 0, nodes, 2000, young.Length);
            }

            // New objects stored at the start of the old array, moved along it by a span copy and by Array.Copy,
            // and cleared where they were, with nothing allocated in between: the object allocated last is young
            // even where the collector runs before every allocation, and the moved element is all that keeps it.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void MoveAlong(Node?[] nodes, int id)
            {
                for (var index = 0; index < 3; index++) nodes[index] = Fresh(id + 3000 + index);
                nodes.AsSpan(0, 3).CopyTo(nodes.AsSpan(3000));
                nodes.AsSpan(0, 3).Clear();
                for (var index = 0; index < 3; index++) nodes[index + 1] = Fresh(id + 3100 + index);
                Array.Copy(nodes, 1, nodes, 3100, 3);
                Array.Clear(nodes, 1, 3);
            }

            // Allocates until the collector has run, then returns new objects.
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static Pair MakePair(int id)
            {
                Churn();
                return new Pair { First = Fresh(id), Second = Fresh(id + 1) };
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Set<T>(ref T location, T value) => location = value;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void SetNode(ref Node? location, Node value) => location = value;

            // Allocates until the collector has run, then more, filling what it allocates: an object freed too
            // early is overwritten by an array of -1.
            [MethodImpl(MethodImplOptions.NoInlining)]
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
                s_filler = filler;
            }
        }
        """;
}
