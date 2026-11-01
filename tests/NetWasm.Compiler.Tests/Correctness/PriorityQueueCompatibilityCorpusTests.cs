namespace NetWasm.Compiler.Tests.Correctness;

public sealed class PriorityQueueCompatibilityCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void PriorityQueuesMatchDesktopAcrossCompilerProfilesAndTargets() =>
        runner.Run(new(
            "PriorityQueueCompatibilityCorpus",
            "NetWasm.Correctness.PriorityQueues",
            """
            using System;
            using System.Collections;
            using System.Collections.Generic;

            namespace NetWasm.Correctness.PriorityQueues;

            public sealed class DescendingComparer : IComparer<int>
            {
                public int Compare(int left, int right) => right.CompareTo(left);
            }

            public sealed class LengthComparer : IEqualityComparer<string>
            {
                public bool Equals(string? left, string? right) => left?.Length == right?.Length;
                public int GetHashCode(string value) => value.Length;
            }

            public static class EntryPoint
            {
                public static int Run(int input) => input switch
                {
                    0 => ExerciseDefaultOrdering(),
                    1 => ExerciseCustomComparer(),
                    2 => ConstructFromItems(),
                    3 => EnqueueRanges(),
                    4 => ExerciseTrySurface(),
                    5 => ExerciseCombinedOperations(),
                    6 => ExerciseRemoval(),
                    7 => ExerciseCapacity(),
                    8 => ExerciseUnorderedItems(),
                    9 => ExerciseEnumerationVersioning(),
                    _ => ValidateFailures(),
                };

                private static int ExerciseDefaultOrdering()
                {
                    var queue = new PriorityQueue<int, int>();
                    queue.Enqueue(50, 5);
                    queue.Enqueue(10, 1);
                    queue.Enqueue(30, 3);
                    queue.Enqueue(20, 2);
                    queue.Enqueue(40, 4);
                    int result = 0;
                    while (queue.Count != 0) result = result * 10 + queue.Dequeue() / 10;
                    return result;
                }

                private static int ExerciseCustomComparer()
                {
                    var comparer = new DescendingComparer();
                    var queue = new PriorityQueue<int, int>(2, comparer);
                    queue.Enqueue(10, 1);
                    queue.Enqueue(30, 3);
                    queue.Enqueue(20, 2);
                    return ReferenceEquals(queue.Comparer, comparer)
                        ? queue.Dequeue() * 10000 + queue.Dequeue() * 100 + queue.Dequeue()
                        : -1;
                }

                private static int ConstructFromItems()
                {
                    var queue = new PriorityQueue<int, int>(new[]
                    {
                        (Element: 40, Priority: 4),
                        (Element: 10, Priority: 1),
                        (Element: 30, Priority: 3),
                        (Element: 20, Priority: 2),
                    });
                    int unordered = 0;
                    foreach (var item in queue.UnorderedItems) unordered += item.Element + item.Priority;
                    return queue.Count * 100000 + queue.Peek() * 1000 + unordered;
                }

                private static int EnqueueRanges()
                {
                    var queue = new PriorityQueue<int, int>();
                    queue.EnqueueRange(new[] { (30, 3), (10, 1), (20, 2) });
                    queue.EnqueueRange(new[] { 40, 50 }, 4);
                    int result = 0;
                    while (queue.TryDequeue(out int element, out int priority))
                    {
                        result += element * priority;
                    }
                    return result;
                }

                private static int ExerciseTrySurface()
                {
                    var queue = new PriorityQueue<string, int>();
                    bool emptyPeek = queue.TryPeek(out string? emptyElement, out int emptyPriority);
                    bool emptyDequeue = queue.TryDequeue(out string? removedElement, out int removedPriority);
                    queue.Enqueue("two", 2);
                    queue.Enqueue("one", 1);
                    bool peek = queue.TryPeek(out string? element, out int priority);
                    return !emptyPeek && !emptyDequeue && emptyElement is null && removedElement is null
                        && emptyPriority == 0 && removedPriority == 0 && peek && element == "one" && priority == 1
                        ? queue.Count
                        : -1;
                }

                private static int ExerciseCombinedOperations()
                {
                    var queue = new PriorityQueue<int, int>();
                    queue.Enqueue(20, 2);
                    queue.Enqueue(40, 4);
                    int notInserted = queue.EnqueueDequeue(50, 1);
                    int inserted = queue.EnqueueDequeue(30, 3);
                    int replaced = queue.DequeueEnqueue(10, 1);
                    return notInserted * 1000000 + inserted * 10000 + replaced * 100 + queue.Dequeue();
                }

                private static int ExerciseRemoval()
                {
                    var queue = new PriorityQueue<string, int>();
                    queue.Enqueue("one", 1);
                    queue.Enqueue("three", 3);
                    queue.Enqueue("four", 4);
                    bool removedByLength = queue.Remove("two", out string? removed, out int priority, new LengthComparer());
                    bool missing = queue.Remove("missing", out string? absent, out int absentPriority);
                    return removedByLength && !missing && removed is not null && removed.Length == 3
                        && priority == 1 && absent is null && absentPriority == 0
                        ? queue.Count
                        : -1;
                }

                private static int ExerciseCapacity()
                {
                    var queue = new PriorityQueue<int, int>(1);
                    int ensured = queue.EnsureCapacity(9);
                    queue.Enqueue(1, 1);
                    queue.Enqueue(2, 2);
                    queue.TrimExcess();
                    int trimmed = queue.Capacity;
                    queue.Clear();
                    return ensured * 10000 + trimmed * 100 + queue.Capacity * 10 + queue.Count;
                }

                private static int ExerciseUnorderedItems()
                {
                    var queue = new PriorityQueue<int, int>();
                    queue.Enqueue(10, 1);
                    queue.Enqueue(20, 2);
                    ICollection items = queue.UnorderedItems;
                    var copy = new object[4];
                    items.CopyTo(copy, 1);
                    var first = ((int Element, int Priority))copy[1];
                    var second = ((int Element, int Priority))copy[2];
                    return !items.IsSynchronized && ReferenceEquals(items.SyncRoot, items)
                        ? first.Element + first.Priority + second.Element + second.Priority
                        : -1;
                }

                private static int ExerciseEnumerationVersioning()
                {
                    var queue = new PriorityQueue<int, int>();
                    IEnumerator empty = ((IEnumerable)queue.UnorderedItems).GetEnumerator();
                    bool emptyResult = empty.MoveNext();
                    queue.Enqueue(10, 1);
                    queue.Enqueue(20, 2);
                    IEnumerator enumerator = ((IEnumerable)queue.UnorderedItems).GetEnumerator();
                    enumerator.MoveNext();
                    queue.Enqueue(30, 3);
                    try
                    {
                        enumerator.MoveNext();
                        return -1;
                    }
                    catch (InvalidOperationException exception)
                    {
                        return !emptyResult && exception.Message == "Collection was modified after the enumerator was instantiated."
                            ? 1
                            : -1;
                    }
                }

                private static int ValidateFailures()
                {
                    int score = 0;
                    var queue = new PriorityQueue<int, int>();
                    try { queue.Peek(); }
                    catch (InvalidOperationException exception)
                    {
                        score += exception.Message == "Queue empty." ? 1 : 0;
                    }
                    try { queue.Dequeue(); }
                    catch (InvalidOperationException exception)
                    {
                        score += exception.Message == "Queue empty." ? 10 : 0;
                    }
                    try { queue.DequeueEnqueue(1, 1); }
                    catch (InvalidOperationException exception)
                    {
                        score += exception.Message == "Queue empty." ? 100 : 0;
                    }
                    try { new PriorityQueue<int, int>(-1); }
                    catch (ArgumentOutOfRangeException exception)
                    {
                        score += exception.ParamName == "initialCapacity" ? 1000 : 0;
                    }
                    try { ((ICollection)queue.UnorderedItems).CopyTo(new int[1], 0); }
                    catch (ArgumentException exception)
                    {
                        score += exception.ParamName == "array" ? 10000 : 0;
                    }
                    return score;
                }

                public static int Trace() => 0;
            }
            """,
            [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10])
        {
            CaptureCompilerDiagnostics = true,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });
}
