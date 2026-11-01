namespace NetWasm.Compiler.Tests.Correctness;

public sealed class StackCompatibilityCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void StacksMatchDesktopAcrossCompilerProfilesAndTargets() =>
        runner.Run(new(
            "StackCompatibilityCorpus",
            "NetWasm.Correctness.Stacks",
            """
            using System;
            using System.Collections;
            using System.Collections.Generic;

            namespace NetWasm.Correctness.Stacks;

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
                    2 => GrowTrimAndClear(),
                    3 => PushPopAndPeek(),
                    4 => ExerciseGenericCopy(),
                    5 => ExerciseNonGenericCopy(),
                    6 => ExerciseEnumeration(),
                    7 => DetectEnumeratorMutation(),
                    8 => ResetAndDisposeEnumerator(),
                    9 => ExerciseReferenceValues(),
                    _ => ValidateFailures(),
                };

                private static int ConstructFromCollection()
                {
                    ICollection<int> source = new[] { 2, 4, 6 };
                    var stack = new Stack<int>(source);
                    return stack.Count * 10000 + stack.Capacity * 100 + Checksum(stack);
                }

                private static int ConstructFromEnumerable()
                {
                    var stack = new Stack<int>(new CountlessEnumerable(1, 3, 5, 7, 9));
                    return stack.Count * 10000 + stack.Capacity * 100 + Checksum(stack);
                }

                private static int GrowTrimAndClear()
                {
                    var stack = new Stack<int>();
                    int ensured = stack.EnsureCapacity(19);
                    for (int value = 0; value < 7; value++) stack.Push(value);
                    stack.TrimExcess();
                    int trimmed = stack.Capacity;
                    stack.TrimExcess(11);
                    int explicitCapacity = stack.Capacity;
                    stack.Clear();
                    return ensured * 10000 + trimmed * 100 + explicitCapacity * 10 + stack.Count;
                }

                private static int PushPopAndPeek()
                {
                    var stack = new Stack<int>(2);
                    stack.Push(2);
                    stack.Push(4);
                    stack.Push(6);
                    bool foundPeek = stack.TryPeek(out int peek);
                    bool foundPop = stack.TryPop(out int popped);
                    int next = stack.Pop();
                    stack.Pop();
                    bool emptyPeek = stack.TryPeek(out int missingPeek);
                    bool emptyPop = stack.TryPop(out int missingPop);
                    return foundPeek && foundPop && !emptyPeek && !emptyPop
                        && peek == 6 && popped == 6 && next == 4
                        && missingPeek == 0 && missingPop == 0
                        ? stack.Count + stack.Capacity
                        : -1;
                }

                private static int ExerciseGenericCopy()
                {
                    var stack = new Stack<int>(new[] { 2, 4, 6 });
                    var copy = new int[6];
                    stack.CopyTo(copy, 2);
                    return Checksum(copy) + (stack.Contains(4) ? 100000 : 0) + (stack.Contains(5) ? 10000 : 0);
                }

                private static int ExerciseNonGenericCopy()
                {
                    ICollection stack = new Stack<int>(new[] { 3, 5, 7 });
                    var copy = new object[5];
                    stack.CopyTo(copy, 1);
                    int score = (int)copy[1] * 100 + (int)copy[2] * 10 + (int)copy[3];
                    score += !stack.IsSynchronized && ReferenceEquals(stack.SyncRoot, stack) ? 1000 : 0;
                    try
                    {
                        stack.CopyTo(new string[3], 0);
                        return -1;
                    }
                    catch (ArgumentException exception)
                    {
                        return exception.ParamName == "array" ? score + 10000 : -1;
                    }
                }

                private static int ExerciseEnumeration()
                {
                    var stack = new Stack<int>(new[] { 1, 2, 3 });
                    int direct = Checksum(stack);
                    int array = Checksum(stack.ToArray());
                    int empty = new Stack<int>().ToArray().Length;
                    return direct * 1000 + array * 10 + empty;
                }

                private static int DetectEnumeratorMutation()
                {
                    var stack = new Stack<int>(new[] { 1, 2 });
                    var enumerator = stack.GetEnumerator();
                    enumerator.MoveNext();
                    stack.Push(3);
                    try
                    {
                        enumerator.MoveNext();
                        return -1;
                    }
                    catch (InvalidOperationException)
                    {
                        return stack.Count;
                    }
                }

                private static int ResetAndDisposeEnumerator()
                {
                    var stack = new Stack<int>(new[] { 4, 5 });
                    IEnumerator enumerator = ((IEnumerable)stack).GetEnumerator();
                    object before = enumerator.Current;
                    enumerator.MoveNext();
                    int first = (int)enumerator.Current;
                    enumerator.Reset();
                    enumerator.MoveNext();
                    int reset = (int)enumerator.Current;
                    ((IDisposable)enumerator).Dispose();
                    bool afterDispose = enumerator.MoveNext();
                    return before is null && !afterDispose ? first * 10 + reset : -1;
                }

                private static int ExerciseReferenceValues()
                {
                    var stack = new Stack<string?>();
                    stack.Push("first");
                    stack.Push(null);
                    bool found = stack.TryPop(out string? value);
                    stack.Clear();
                    return found && value is null && stack.Count == 0 && !stack.Contains("first") ? stack.Capacity : -1;
                }

                private static int ValidateFailures()
                {
                    int score = 0;
                    try { _ = new Stack<int>(-1); }
                    catch (ArgumentOutOfRangeException exception) { score += exception.ParamName == "capacity" ? 1 : 0; }
                    try { _ = new Stack<int>(null!); }
                    catch (ArgumentNullException exception) { score += exception.ParamName == "collection" ? 10 : 0; }

                    var stack = new Stack<int>();
                    try { stack.Pop(); }
                    catch (InvalidOperationException exception) { score += exception.Message == "Stack empty." ? 100 : 0; }
                    try { stack.Peek(); }
                    catch (InvalidOperationException exception) { score += exception.Message == "Stack empty." ? 1000 : 0; }
                    try { stack.CopyTo([], -1); }
                    catch (ArgumentOutOfRangeException exception) { score += exception.ParamName == "arrayIndex" ? 10000 : 0; }

                    stack.Push(1);
                    try { stack.TrimExcess(0); }
                    catch (ArgumentOutOfRangeException exception) { score += exception.ParamName == "capacity" ? 100000 : 0; }
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
