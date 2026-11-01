namespace NetWasm.Compiler.Tests.Correctness;

public sealed class LinkedListCompatibilityCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void LinkedListsMatchDesktopAcrossCompilerProfilesAndTargets() =>
        runner.Run(new(
            "LinkedListCompatibilityCorpus",
            "NetWasm.Correctness.LinkedLists",
            """
            using System;
            using System.Collections;
            using System.Collections.Generic;

            namespace NetWasm.Correctness.LinkedLists;

            public static class EntryPoint
            {
                public static int Run(int input) => input switch
                {
                    0 => ConstructAndSearch(),
                    1 => ExerciseNodeOverloads(),
                    2 => ExerciseGenericCollection(),
                    3 => ExerciseNonGenericCollection(),
                    4 => RemoveAndClear(),
                    5 => FindNullValues(),
                    6 => ExerciseEnumeratorState(),
                    7 => DetectEnumeratorMutation(),
                    8 => MutateValueByReference(),
                    _ => ValidateFailures(),
                };

                private static int ConstructAndSearch()
                {
                    var list = new LinkedList<int>(new[] { 2, 4, 6, 4 });
                    return list.Count * 100000
                        + list.First!.Value * 10000
                        + list.Last!.Value * 1000
                        + list.Find(4)!.Previous!.Value * 100
                        + list.FindLast(4)!.Previous!.Value * 10
                        + (list.Contains(6) && !list.Contains(9) ? 1 : 0);
                }

                private static int ExerciseNodeOverloads()
                {
                    var list = new LinkedList<int>();
                    var two = new LinkedListNode<int>(2);
                    var one = new LinkedListNode<int>(1);
                    var three = new LinkedListNode<int>(3);
                    var four = new LinkedListNode<int>(4);
                    list.AddFirst(two);
                    list.AddBefore(two, one);
                    list.AddAfter(two, three);
                    list.AddLast(four);
                    list.Remove(three);
                    var other = new LinkedList<int>();
                    other.AddFirst(three);
                    return Checksum(list) * 1000
                        + Checksum(other) * 10
                        + (three.List == other && three.Next is null && three.Previous is null ? 1 : 0);
                }

                private static int ExerciseGenericCollection()
                {
                    ICollection<int> list = new LinkedList<int>();
                    list.Add(3);
                    list.Add(5);
                    list.Add(7);
                    var copy = new int[6];
                    list.CopyTo(copy, 2);
                    bool removed = list.Remove(5);
                    return (list.IsReadOnly ? 100000 : 0)
                        + (removed ? 10000 : 0)
                        + list.Count * 1000
                        + Checksum(copy);
                }

                private static int ExerciseNonGenericCollection()
                {
                    ICollection list = new LinkedList<int>(new[] { 3, 5, 7 });
                    var copy = new object[5];
                    list.CopyTo(copy, 1);
                    int score = (int)copy[1] * 100 + (int)copy[2] * 10 + (int)copy[3];
                    score += !list.IsSynchronized && ReferenceEquals(list.SyncRoot, list) ? 1000 : 0;
                    try
                    {
                        list.CopyTo(new string[3], 0);
                        return -1;
                    }
                    catch (ArgumentException exception)
                    {
                        return exception.ParamName == "array" ? score + 10000 : -1;
                    }
                }

                private static int RemoveAndClear()
                {
                    var list = new LinkedList<int>(new[] { 1, 2, 3, 4 });
                    list.RemoveFirst();
                    list.RemoveLast();
                    bool removed = list.Remove(2);
                    var remaining = list.First!;
                    list.Clear();
                    return removed && list.Count == 0 && list.First is null && list.Last is null
                        && remaining.List is null && remaining.Next is null && remaining.Previous is null
                        ? 1
                        : -1;
                }

                private static int FindNullValues()
                {
                    var list = new LinkedList<string?>();
                    list.AddLast("first");
                    var firstNull = list.AddLast((string?)null);
                    list.AddLast("last");
                    var lastNull = list.AddLast((string?)null);
                    return ReferenceEquals(list.Find(null), firstNull)
                        && ReferenceEquals(list.FindLast(null), lastNull)
                        && list.Contains(null)
                        ? list.Count
                        : -1;
                }

                private static int ExerciseEnumeratorState()
                {
                    var list = new LinkedList<int>(new[] { 4, 5 });
                    IEnumerator enumerator = ((IEnumerable)list).GetEnumerator();
                    int score = 0;
                    try { _ = enumerator.Current; }
                    catch (InvalidOperationException exception)
                    {
                        score += exception.Message == "Enumeration has either not started or has already finished." ? 1 : 0;
                    }
                    enumerator.MoveNext();
                    score += (int)enumerator.Current * 10;
                    enumerator.Reset();
                    enumerator.MoveNext();
                    score += (int)enumerator.Current * 100;
                    enumerator.MoveNext();
                    enumerator.MoveNext();
                    try { _ = enumerator.Current; }
                    catch (InvalidOperationException) { score += 1000; }
                    ((IDisposable)enumerator).Dispose();
                    return score;
                }

                private static int DetectEnumeratorMutation()
                {
                    var list = new LinkedList<int>(new[] { 1, 2 });
                    var enumerator = list.GetEnumerator();
                    enumerator.MoveNext();
                    list.AddLast(3);
                    try
                    {
                        enumerator.MoveNext();
                        return -1;
                    }
                    catch (InvalidOperationException exception)
                    {
                        return exception.Message == "Collection was modified after the enumerator was instantiated."
                            ? list.Count
                            : -1;
                    }
                }

                private static int MutateValueByReference()
                {
                    var node = new LinkedListNode<int>(3);
                    ref int value = ref node.ValueRef;
                    value = 9;
                    var list = new LinkedList<int>();
                    list.AddFirst(node);
                    return list.First!.Value;
                }

                private static int ValidateFailures()
                {
                    int score = 0;
                    try { _ = new LinkedList<int>(null!); }
                    catch (ArgumentNullException exception) { score += exception.ParamName == "collection" ? 1 : 0; }

                    var list = new LinkedList<int>();
                    try { list.RemoveFirst(); }
                    catch (InvalidOperationException exception) { score += exception.Message == "The LinkedList is empty." ? 10 : 0; }
                    try { list.RemoveLast(); }
                    catch (InvalidOperationException exception) { score += exception.Message == "The LinkedList is empty." ? 100 : 0; }
                    try { list.CopyTo([], -1); }
                    catch (ArgumentOutOfRangeException exception) { score += exception.ParamName == "index" ? 1000 : 0; }

                    var attached = list.AddLast(1);
                    try { list.AddFirst(attached); }
                    catch (InvalidOperationException exception) { score += exception.Message == "The LinkedList node already belongs to a LinkedList." ? 10000 : 0; }

                    var other = new LinkedList<int>();
                    var external = other.AddLast(2);
                    try { list.Remove(external); }
                    catch (InvalidOperationException exception) { score += exception.Message == "The LinkedList node does not belong to current LinkedList." ? 100000 : 0; }
                    return score;
                }

                private static int Checksum(IEnumerable<int> values)
                {
                    int result = 0;
                    foreach (int value in values) result = result * 11 + value;
                    return result;
                }

                public static int Trace() => 0;
            }
            """,
            [0, 1, 2, 3, 4, 5, 6, 7, 8, 9])
        {
            CaptureCompilerDiagnostics = true,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });
}
