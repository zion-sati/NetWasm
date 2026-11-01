namespace NetWasm.Compiler.Tests.Correctness;

public sealed class QueueCompatibilityCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void QueuesMatchDesktopAcrossCompilerProfilesAndTargets() =>
        runner.Run(new(
            "QueueCompatibilityCorpus",
            "NetWasm.Correctness.Queues",
            """
            using System;
            using System.Collections;
            using System.Collections.Generic;

            namespace NetWasm.Correctness.Queues;

            public sealed class CountlessEnumerable : IEnumerable<int>
            {
                private readonly int[] _values;

                public CountlessEnumerable(params int[] values) => _values = values;

                public IEnumerator<int> GetEnumerator() => new Enumerator(_values);
                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

                private sealed class Enumerator : IEnumerator<int>
                {
                    private readonly int[] _values;
                    private int _index = -1;

                    public Enumerator(int[] values) => _values = values;
                    public int Current => _values[_index];
                    object IEnumerator.Current => Current;
                    public bool MoveNext() => ++_index < _values.Length;
                    public void Reset() => _index = -1;
                    public void Dispose() { }
                }
            }

            public static class EntryPoint
            {
                public static int Run(int input) => input switch
                {
                    0 => ConstructFromCollection(),
                    1 => ConstructFromEnumerable(),
                    2 => WrapAroundAndCopy(),
                    3 => GrowTrimAndClear(),
                    4 => ExerciseGenericCopy(),
                    5 => ExerciseNonGenericCopy(),
                    6 => PeekAndDequeue(),
                    7 => DetectEnumeratorMutation(),
                    8 => ResetEnumerator(),
                    9 => PreserveEnumeratorWithoutResize(),
                    _ => ValidateFailures(),
                };

                private static int ConstructFromCollection()
                {
                    ICollection<int> source = new[] { 2, 4, 6 };
                    var queue = new Queue<int>(source);
                    return queue.Count * 10000 + queue.Capacity * 100 + Checksum(queue);
                }

                private static int ConstructFromEnumerable()
                {
                    var queue = new Queue<int>(new CountlessEnumerable(1, 3, 5, 7, 9));
                    return queue.Count * 10000 + queue.Capacity * 100 + Checksum(queue);
                }

                private static int WrapAroundAndCopy()
                {
                    var queue = new Queue<int>(4);
                    queue.Enqueue(1);
                    queue.Enqueue(2);
                    queue.Enqueue(3);
                    queue.Enqueue(4);
                    queue.Dequeue();
                    queue.Dequeue();
                    queue.Enqueue(5);
                    queue.Enqueue(6);
                    int[] copy = queue.ToArray();
                    return (queue.Contains(3) ? 100000 : 0)
                        + (queue.Contains(1) ? 10000 : 0)
                        + Checksum(copy);
                }

                private static int GrowTrimAndClear()
                {
                    var queue = new Queue<int>();
                    int ensured = queue.EnsureCapacity(19);
                    for (int value = 0; value < 7; value++) queue.Enqueue(value);
                    queue.TrimExcess();
                    int trimmed = queue.Capacity;
                    queue.Clear();
                    return ensured * 10000 + trimmed * 100 + queue.Count * 10 + queue.Capacity;
                }

                private static int ExerciseGenericCopy()
                {
                    var queue = new Queue<int>(new[] { 2, 4, 6 });
                    var copy = new int[6];
                    queue.CopyTo(copy, 2);
                    return Checksum(copy);
                }

                private static int ExerciseNonGenericCopy()
                {
                    ICollection queue = new Queue<int>(new[] { 3, 5, 7 });
                    var copy = new object[5];
                    queue.CopyTo(copy, 1);
                    int score = (int)copy[1] * 100 + (int)copy[2] * 10 + (int)copy[3];
                    try
                    {
                        queue.CopyTo(new string[3], 0);
                        return -1;
                    }
                    catch (ArgumentException)
                    {
                        return score;
                    }
                }

                private static int PeekAndDequeue()
                {
                    var queue = new Queue<string?>();
                    bool emptyPeek = queue.TryPeek(out var missingPeek);
                    bool emptyDequeue = queue.TryDequeue(out var missingDequeue);
                    queue.Enqueue(null);
                    queue.Enqueue("value");
                    bool foundPeek = queue.TryPeek(out var first);
                    bool foundDequeue = queue.TryDequeue(out var removed);
                    return !emptyPeek && !emptyDequeue
                        && missingPeek is null && missingDequeue is null
                        && foundPeek && foundDequeue && first is null && removed is null
                        && queue.Peek() == "value"
                        ? queue.Count
                        : -1;
                }

                private static int DetectEnumeratorMutation()
                {
                    var queue = new Queue<int>(new[] { 1, 2 });
                    var enumerator = queue.GetEnumerator();
                    enumerator.MoveNext();
                    queue.Enqueue(3);
                    try
                    {
                        enumerator.MoveNext();
                        return -1;
                    }
                    catch (InvalidOperationException)
                    {
                        return queue.Count;
                    }
                }

                private static int ResetEnumerator()
                {
                    var queue = new Queue<int>(new[] { 4, 5 });
                    IEnumerator enumerator = ((IEnumerable)queue).GetEnumerator();
                    enumerator.MoveNext();
                    int first = (int)enumerator.Current;
                    enumerator.Reset();
                    enumerator.MoveNext();
                    return first * 10 + (int)enumerator.Current;
                }

                private static int PreserveEnumeratorWithoutResize()
                {
                    var queue = new Queue<int>(8);
                    queue.Enqueue(1);
                    queue.Enqueue(2);
                    var enumerator = queue.GetEnumerator();
                    enumerator.MoveNext();
                    queue.TrimExcess(queue.Capacity);
                    return enumerator.MoveNext() ? enumerator.Current : -1;
                }

                private static int ValidateFailures()
                {
                    int score = 0;
                    try { _ = new Queue<int>(-1); }
                    catch (ArgumentOutOfRangeException exception) { score += exception.ParamName == "capacity" ? 1 : 0; }
                    try { _ = new Queue<int>(null!); }
                    catch (ArgumentNullException exception) { score += exception.ParamName == "collection" ? 10 : 0; }

                    var queue = new Queue<int>();
                    try { queue.Dequeue(); }
                    catch (InvalidOperationException exception) { score += exception.Message == "Queue empty." ? 100 : 0; }
                    try { queue.Peek(); }
                    catch (InvalidOperationException exception) { score += exception.Message == "Queue empty." ? 1000 : 0; }
                    try { queue.CopyTo([], -1); }
                    catch (ArgumentOutOfRangeException exception) { score += exception.ParamName == "arrayIndex" ? 10000 : 0; }
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
            [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10])
        {
            CaptureCompilerDiagnostics = true,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });
}
