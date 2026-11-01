namespace NetWasm.Compiler.Tests.Correctness;

public sealed class SortedListCompatibilityCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void SortedListsMatchDesktopAcrossCompilerProfilesAndTargets() =>
        runner.Run(new(
            "SortedListCompatibilityCorpus",
            "NetWasm.Correctness.SortedLists",
            """
            using System;
            using System.Collections;
            using System.Collections.Generic;

            namespace NetWasm.Correctness.SortedLists;

            public sealed class DescendingComparer : IComparer<int>
            {
                public int Compare(int left, int right) => right.CompareTo(left);
            }

            public static class EntryPoint
            {
                public static int Run(int input) => input switch
                {
                    0 => ConstructWithComparer(),
                    1 => ConstructFromDictionary(),
                    2 => ExerciseIndexedSurface(),
                    3 => GrowTrimAndClear(),
                    4 => ExerciseGenericCollection(),
                    5 => ExerciseNonGenericDictionary(),
                    6 => ExerciseGenericCopy(),
                    7 => ExerciseNonGenericCopy(),
                    8 => ExerciseViews(),
                    9 => ExerciseEnumerators(),
                    _ => ValidateFailures(),
                };

                private static int ConstructWithComparer()
                {
                    var comparer = new DescendingComparer();
                    var values = new SortedList<int, int>(1, comparer)
                    {
                        [2] = 20,
                        [1] = 10,
                        [3] = 30,
                    };
                    return ReferenceEquals(values.Comparer, comparer)
                        ? Checksum(values.Keys) * 1000 + Checksum(values.Values)
                        : -1;
                }

                private static int ConstructFromDictionary()
                {
                    IDictionary<int, int> source = new Dictionary<int, int>
                    {
                        [4] = 40,
                        [1] = 10,
                        [3] = 30,
                    };
                    var values = new SortedList<int, int>(source);
                    return values.Count * 100000 + Checksum(values.Keys) * 100 + Checksum(values.Values);
                }

                private static int ExerciseIndexedSurface()
                {
                    var values = new SortedList<int, string>
                    {
                        [3] = "three",
                        [1] = "one",
                        [2] = "two",
                    };
                    values.SetValueAtIndex(1, "changed");
                    bool found = values.TryGetValue(3, out string? existing);
                    bool missing = values.TryGetValue(9, out string? absent);
                    return found && !missing && existing == "three" && absent is null
                        && values.GetKeyAtIndex(1) == 2 && values.GetValueAtIndex(1) == "changed"
                        && values.IndexOfKey(3) == 2 && values.IndexOfValue("one") == 0
                        ? values.Count
                        : -1;
                }

                private static int GrowTrimAndClear()
                {
                    var values = new SortedList<int, int>(1)
                    {
                        [1] = 10,
                        [2] = 20,
                        [3] = 30,
                    };
                    int grown = values.Capacity;
                    values.Capacity = 8;
                    values.TrimExcess();
                    int trimmed = values.Capacity;
                    values.Clear();
                    return grown * 10000 + trimmed * 100 + values.Capacity * 10 + values.Count;
                }

                private static int ExerciseGenericCollection()
                {
                    ICollection<KeyValuePair<int, int>> values = new SortedList<int, int>();
                    values.Add(new KeyValuePair<int, int>(2, 20));
                    values.Add(new KeyValuePair<int, int>(1, 10));
                    bool exact = values.Contains(new KeyValuePair<int, int>(1, 10));
                    bool wrongValue = values.Contains(new KeyValuePair<int, int>(1, 11));
                    bool refused = values.Remove(new KeyValuePair<int, int>(2, 21));
                    bool removed = values.Remove(new KeyValuePair<int, int>(2, 20));
                    return !values.IsReadOnly && exact && !wrongValue && !refused && removed
                        ? values.Count
                        : -1;
                }

                private static int ExerciseNonGenericDictionary()
                {
                    IDictionary values = new SortedList<int, string>();
                    values.Add(2, "two");
                    values[1] = "one";
                    values[3] = "three";
                    IDictionaryEnumerator enumerator = values.GetEnumerator();
                    enumerator.MoveNext();
                    int first = (int)enumerator.Key;
                    string firstValue = (string)enumerator.Value!;
                    values.Remove(2);
                    return !values.IsReadOnly && !values.IsFixedSize
                        && values.Contains(1) && !values.Contains("bad")
                        && first == 1 && firstValue == "one"
                        ? values.Count
                        : -1;
                }

                private static int ExerciseGenericCopy()
                {
                    ICollection<KeyValuePair<int, int>> values = new SortedList<int, int>
                    {
                        [2] = 20,
                        [1] = 10,
                    };
                    var copy = new KeyValuePair<int, int>[4];
                    values.CopyTo(copy, 1);
                    return copy[1].Key * 10000 + copy[1].Value * 100 + copy[2].Key * 10 + copy[2].Value;
                }

                private static int ExerciseNonGenericCopy()
                {
                    ICollection values = new SortedList<int, int>
                    {
                        [2] = 20,
                        [1] = 10,
                    };
                    var copy = new object[4];
                    values.CopyTo(copy, 1);
                    var first = (KeyValuePair<int, int>)copy[1];
                    var second = (KeyValuePair<int, int>)copy[2];
                    int score = first.Key * 10000 + first.Value * 100 + second.Key * 10 + second.Value;
                    score += !values.IsSynchronized && ReferenceEquals(values.SyncRoot, values) ? 100000 : 0;
                    try
                    {
                        values.CopyTo(new string[2], 0);
                        return -1;
                    }
                    catch (ArgumentException exception)
                    {
                        return exception.ParamName == "array" ? score + 1000000 : -1;
                    }
                }

                private static int ExerciseViews()
                {
                    var values = new SortedList<int, int>
                    {
                        [2] = 20,
                        [1] = 10,
                        [3] = 30,
                    };
                    IList<int> keys = values.Keys;
                    IList<int> items = values.Values;
                    var keyCopy = new int[5];
                    var valueCopy = new int[5];
                    keys.CopyTo(keyCopy, 1);
                    items.CopyTo(valueCopy, 1);
                    int score = Checksum(keys) * 100000 + Checksum(items) * 10 + Checksum(keyCopy) + Checksum(valueCopy);
                    try
                    {
                        keys.Add(4);
                        return -1;
                    }
                    catch (NotSupportedException exception)
                    {
                        return exception.Message == "This operation is not supported on SortedList nested types because they require modifying the original SortedList."
                            ? score
                            : -1;
                    }
                }

                private static int ExerciseEnumerators()
                {
                    var empty = new SortedList<int, int>();
                    IEnumerator<KeyValuePair<int, int>> emptyEnumerator = empty.GetEnumerator();
                    bool emptyResult = emptyEnumerator.MoveNext();

                    var values = new SortedList<int, int>
                    {
                        [2] = 20,
                        [1] = 10,
                    };
                    IEnumerator enumerator = ((IEnumerable)values).GetEnumerator();
                    int score = 0;
                    try { _ = enumerator.Current; }
                    catch (InvalidOperationException) { score += 1; }
                    enumerator.MoveNext();
                    var first = (KeyValuePair<int, int>)enumerator.Current;
                    score += first.Key * 10;
                    enumerator.Reset();
                    enumerator.MoveNext();
                    score += ((KeyValuePair<int, int>)enumerator.Current).Value * 100;
                    values[3] = 30;
                    try { enumerator.MoveNext(); }
                    catch (InvalidOperationException) { score += 10000; }
                    return !emptyResult ? score : -1;
                }

                private static int ValidateFailures()
                {
                    int score = 0;
                    var values = new SortedList<int, int> { [1] = 10 };
                    try { values.Add(1, 20); }
                    catch (ArgumentException exception)
                    {
                        score += exception.ParamName == "key" && exception.Message.StartsWith("An item with the same key has already been added. Key: 1") ? 1 : 0;
                    }
                    try { values.Capacity = 0; }
                    catch (ArgumentOutOfRangeException exception) { score += exception.ParamName == "value" ? 10 : 0; }
                    try { _ = values.GetValueAtIndex(1); }
                    catch (ArgumentOutOfRangeException exception) { score += exception.ParamName == "index" ? 100 : 0; }

                    IDictionary nongeneric = values;
                    try { nongeneric.Add("bad", 1); }
                    catch (ArgumentException exception)
                    {
                        score += exception.ParamName == "key" ? 1000 : 0;
                    }
                    try { nongeneric.Add(2, "bad"); }
                    catch (ArgumentException exception)
                    {
                        score += exception.ParamName == "value" ? 10000 : 0;
                    }
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
