namespace NetWasm.Compiler.Tests.Correctness;

// A struct element copied out of an array field by field keeps its reference
// field in a local. Once the array slot is cleared, that local alone holds the
// object, which must survive the collections that follow.
[Collection(CorrectnessTestGroup.Name)]
public sealed class StructElementCopyRootingTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void ReferencesInCopiedElementsSurviveCollections() =>
        runner.Run(new(
            "StructElementCopyRooting",
            "NetWasm.Correctness.StructElementCopyRooting",
            """
            using System;
            using System.Runtime.CompilerServices;

            namespace NetWasm.Correctness.StructElementCopyRooting;

            public sealed class Node(int value)
            {
                public int Value = value;
            }

            public struct Entry
            {
                public Node Node;
                public int Weight;
                public long Unread;
            }

            public static class EntryPoint
            {
                private static Node[]? s_sink;
                private static WeakReference? s_watched;
                private static int s_lost;

                public static int Run(int input)
                {
                    var entries = new Entry[6];
                    for (var index = 0; index < entries.Length; index++)
                    {
                        entries[index] = new Entry { Node = new Node(input * 10 + index), Weight = index + 1, Unread = index };
                    }
                    var hash = 17;
                    for (var index = 0; index < entries.Length; index++)
                    {
                        var entry = entries[index];
                        s_watched = new WeakReference(entry.Node);
                        entries[index] = default;
                        Churn();
                        hash = unchecked(hash * 31 + entry.Node.Value * entry.Weight);
                        s_watched = null;
                    }
                    return unchecked(hash * 31 + s_lost);
                }

                // Counts a watched object the collection freed, then refills
                // freed memory so a lost reference reads another node.
                [MethodImpl(MethodImplOptions.NoInlining)]
                private static void Churn()
                {
                    GC.Collect();
                    if (s_watched is { IsAlive: false }) s_lost++;
                    s_sink = new Node[32];
                    for (var index = 0; index < s_sink.Length; index++)
                    {
                        s_sink[index] = new Node(-index - 1000);
                    }
                    GC.Collect();
                }

                public static int Trace() => 0;
            }
            """,
            [0, 1, 7])
        {
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection |
                OracleRuntimeCapabilities.WeakReferenceClearing,
            Matrix = new(CorpusMatrixProfile.Extended, Backend: CorpusExecutionBackend.Linked),
        });
}
