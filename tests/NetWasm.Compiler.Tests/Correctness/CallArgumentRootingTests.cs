namespace NetWasm.Compiler.Tests.Correctness;

// A call hands its object arguments to the compiled callee, which roots each
// one it keeps live across its own collections. An object only the stack
// holds must survive collections in the callee whether it arrives directly,
// through virtual, interface or delegate dispatch, as the receiver or inside
// a struct. A value the caller leaves beneath the arguments stays the caller's
// to root, and so do arguments a type initializer may collect before the call.
[Collection(CorrectnessTestGroup.Name)]
public sealed class CallArgumentRootingTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void ObjectsOnlyTheStackHoldsSurviveCollectionsInTheirCallee() =>
        runner.Run(new(
            "CallArgumentRooting",
            "NetWasm.Correctness.CallArgumentRooting",
            """
            using System;
            using System.Runtime.CompilerServices;

            namespace NetWasm.Correctness.CallArgumentRooting;

            public sealed class Node(int value, Node? next)
            {
                public int Value = value;
                public Node? Next = next;

                [MethodImpl(MethodImplOptions.NoInlining)]
                public Node Self()
                {
                    EntryPoint.Churn();
                    return this;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                public int Length()
                {
                    EntryPoint.Churn();
                    return EntryPoint.Sum(this);
                }
            }

            public readonly struct Holder(Node node, int weight)
            {
                public readonly Node Node = node;
                public readonly int Weight = weight;
            }

            public interface IMeasure
            {
                int Measure(Node node);
            }

            public class Measurer : IMeasure
            {
                [MethodImpl(MethodImplOptions.NoInlining)]
                public virtual int Measure(Node node)
                {
                    EntryPoint.Churn();
                    return EntryPoint.Sum(node);
                }
            }

            public sealed class DoubleMeasurer : Measurer
            {
                [MethodImpl(MethodImplOptions.NoInlining)]
                public override int Measure(Node node)
                {
                    EntryPoint.Churn();
                    return 2 * EntryPoint.Sum(node);
                }
            }

            public static class Initialized
            {
                private static readonly Node s_seed;

                // An explicit initializer runs at the first call, while the
                // call's arguments are still the caller's.
                static Initialized()
                {
                    EntryPoint.Churn();
                    s_seed = EntryPoint.Chain(99, 2);
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                public static int Use(Node node) => EntryPoint.Sum(node) + s_seed.Value;
            }

            public static class EntryPoint
            {
                private static Node? s_sink;
                private static WeakReference? s_watched;
                private static int s_lost;

                public static int Run(int input)
                {
                    var hash = 17;
                    IMeasure measure = (input & 1) == 0 ? new Measurer() : new DoubleMeasurer();
                    Func<Node, int> forward = Direct;
                    hash = Mix(hash, Initialized.Use(Watch(Chain(input + 5, 4))));
                    Unwatch();
                    for (var round = 0; round < 3; round++)
                    {
                        hash = Mix(hash, Direct(Watch(Chain(input + round, 6))));
                        Unwatch();
                        hash = Mix(hash, measure.Measure(Watch(Chain(input * 3 + round, 5))));
                        Unwatch();
                        hash = Mix(hash, ((Measurer)measure).Measure(Watch(Chain(input - round, 4))));
                        Unwatch();
                        hash = Mix(hash, Watch(Chain(input + 7, 3)).Self().Length());
                        Unwatch();
                        hash = Mix(hash, forward(Watch(Chain(input ^ round, 3))));
                        Unwatch();
                        hash = Mix(hash, ByValue(new Holder(Watch(Chain(input + 2, 3)), round + 1)));
                        Unwatch();
                        hash = Mix(hash, Generic(Watch(Chain(round, 2)), input));
                        Unwatch();
                        // The first chain stays beneath the second call's argument.
                        hash = Mix(hash, Pair(Watch(Chain(input, 2)), Direct(Chain(input + 1, 3))));
                        Unwatch();
                    }
                    return Mix(hash, s_lost);
                }

                // The watched object is reachable whenever a collection runs.
                private static Node Watch(Node node)
                {
                    s_watched = new WeakReference(node);
                    return node;
                }

                private static void Unwatch() => s_watched = null;

                public static Node Chain(int seed, int length)
                {
                    Node? node = null;
                    for (var index = 0; index < length; index++)
                    {
                        node = new Node(seed * 10 + index, node);
                    }
                    return node!;
                }

                public static int Sum(Node? node)
                {
                    var sum = 0;
                    for (; node is not null; node = node.Next)
                    {
                        sum = unchecked(sum * 7 + node.Value);
                    }
                    return sum;
                }

                // Counts a watched object the collection freed, then refills
                // freed memory so a lost reference reads other nodes' fields.
                [MethodImpl(MethodImplOptions.NoInlining)]
                public static void Churn()
                {
                    GC.Collect();
                    if (s_watched is { IsAlive: false }) s_lost++;
                    for (var index = 0; index < 64; index++)
                    {
                        s_sink = new Node(-index - 1000, s_sink is null || (index & 7) == 0 ? null : s_sink);
                    }
                    GC.Collect();
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int Direct(Node node)
                {
                    Churn();
                    return Sum(node);
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int ByValue(Holder holder)
                {
                    Churn();
                    return Sum(holder.Node) * holder.Weight;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int Generic<T>(T value, int salt) where T : class
                {
                    Churn();
                    return Sum(value as Node) + salt;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int Pair(Node first, int second)
                {
                    Churn();
                    return unchecked(Sum(first) * 31 + second);
                }

                private static int Mix(int hash, int value) => unchecked(hash * 31 + value);

                public static int Trace() => 0;
            }
            """,
            [0, 1, 4, 9])
        {
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection |
                OracleRuntimeCapabilities.WeakReferenceClearing,
            Matrix = new(CorpusMatrixProfile.Extended, Backend: CorpusExecutionBackend.Linked),
        });
}
