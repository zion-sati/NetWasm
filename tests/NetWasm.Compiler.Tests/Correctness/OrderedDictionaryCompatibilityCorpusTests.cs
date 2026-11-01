namespace NetWasm.Compiler.Tests.Correctness;

public sealed class OrderedDictionaryCompatibilityCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void OrderedDictionariesMatchDesktopAcrossCompilerProfilesAndTargets() =>
        runner.Run(new(
            "OrderedDictionaryCompatibilityCorpus",
            "NetWasm.Correctness.OrderedDictionaries",
            """
            using System;
            using System.Collections;
            using System.Collections.Generic;

            namespace NetWasm.Correctness.OrderedDictionaries;

            public sealed class AbsoluteComparer : IEqualityComparer<int>
            {
                public bool Equals(int left, int right) => Math.Abs(left) == Math.Abs(right);
                public int GetHashCode(int value) => Math.Abs(value);
            }

            public static class EntryPoint
            {
                public static int Run(int input) => input switch
                {
                    0 => ConstructAndAdd(),
                    1 => ExerciseIndexedSurface(),
                    2 => ExerciseRemovalAndShifting(),
                    3 => ExerciseCapacity(),
                    4 => ExerciseGenericInterfaces(),
                    5 => ExerciseNonGenericDictionary(),
                    6 => ExerciseNonGenericList(),
                    7 => ExerciseViews(),
                    8 => ExerciseEnumerators(),
                    9 => ExerciseComparersAndLookup(),
                    _ => ValidateFailures(),
                };

                private static int ConstructAndAdd()
                {
                    var values = new OrderedDictionary<int, int>(2)
                    {
                        [2] = 20,
                        [1] = 10,
                    };
                    bool added = values.TryAdd(3, 30, out int index);
                    bool duplicate = values.TryAdd(2, 99, out int duplicateIndex);
                    return added && !duplicate && index == 2 && duplicateIndex == 0
                        ? Checksum(values) + values.Count * 1000000
                        : -1;
                }

                private static int ExerciseIndexedSurface()
                {
                    var values = new OrderedDictionary<int, int>
                    {
                        [1] = 10,
                        [3] = 30,
                    };
                    values.Insert(1, 2, 20);
                    values.SetAt(1, 22);
                    values.SetAt(2, 4, 40);
                    KeyValuePair<int, int> middle = values.GetAt(1);
                    return middle.Key == 2 && middle.Value == 22 && values.IndexOf(4) == 2
                        ? Checksum(values)
                        : -1;
                }

                private static int ExerciseRemovalAndShifting()
                {
                    var values = new OrderedDictionary<int, int>();
                    for (int i = 0; i < 12; i++) values.Add(i, i * 10);
                    bool removed = values.Remove(3, out int removedValue);
                    values.RemoveAt(0);
                    values.RemoveAt(values.Count - 1);
                    bool missing = values.Remove(99, out int missingValue);
                    return removed && removedValue == 30 && !missing && missingValue == 0
                        ? values.Count * 100000 + values.GetAt(0).Key * 1000 + values.GetAt(values.Count - 1).Key
                        : -1;
                }

                private static int ExerciseCapacity()
                {
                    var values = new OrderedDictionary<int, int>(1);
                    int ensured = values.EnsureCapacity(40);
                    for (int i = 0; i < 20; i++) values.Add(i, i);
                    values.TrimExcess();
                    int trimmed = values.Capacity;
                    values.TrimExcess(30);
                    int explicitCapacity = values.Capacity;
                    values.Clear();
                    return ensured * 1000000 + trimmed * 1000 + explicitCapacity * 10 + values.Count;
                }

                private static int ExerciseGenericInterfaces()
                {
                    IDictionary<int, int> dictionary = new OrderedDictionary<int, int>();
                    dictionary.Add(2, 20);
                    dictionary.Add(1, 10);
                    IList<KeyValuePair<int, int>> list = (IList<KeyValuePair<int, int>>)dictionary;
                    list.Insert(1, new KeyValuePair<int, int>(3, 30));
                    list[0] = new KeyValuePair<int, int>(4, 40);
                    bool contains = list.Contains(new KeyValuePair<int, int>(3, 30));
                    bool removed = list.Remove(new KeyValuePair<int, int>(1, 10));
                    return contains && removed ? Checksum((OrderedDictionary<int, int>)dictionary) : -1;
                }

                private static int ExerciseNonGenericDictionary()
                {
                    IDictionary dictionary = new OrderedDictionary<int, string>();
                    dictionary.Add(2, "two");
                    dictionary[1] = "one";
                    IDictionaryEnumerator enumerator = dictionary.GetEnumerator();
                    enumerator.MoveNext();
                    DictionaryEntry first = enumerator.Entry;
                    dictionary.Remove(2);
                    return !dictionary.IsReadOnly && !dictionary.IsFixedSize
                        && dictionary.Contains(1) && !dictionary.Contains("bad")
                        && (int)first.Key == 2 && (string)first.Value! == "two"
                        ? dictionary.Count
                        : -1;
                }

                private static int ExerciseNonGenericList()
                {
                    IList list = new OrderedDictionary<int, int>();
                    int first = list.Add(new KeyValuePair<int, int>(1, 10));
                    list.Insert(0, new KeyValuePair<int, int>(2, 20));
                    list[1] = new KeyValuePair<int, int>(3, 30);
                    bool contains = list.Contains(new KeyValuePair<int, int>(2, 20));
                    int index = list.IndexOf(new KeyValuePair<int, int>(3, 30));
                    list.RemoveAt(0);
                    return first == 0 && contains && index == 1 ? list.Count : -1;
                }

                private static int ExerciseViews()
                {
                    var values = new OrderedDictionary<int, int>
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
                    return keys[0] == 2 && items[1] == 10 && keys.IndexOf(3) == 2 && items.Contains(30)
                        ? Checksum(keys) * 100000 + Checksum(items) * 10 + Checksum(keyCopy) + Checksum(valueCopy)
                        : -1;
                }

                private static int ExerciseEnumerators()
                {
                    var empty = new OrderedDictionary<int, int>();
                    bool emptyResult = ((IEnumerable<KeyValuePair<int, int>>)empty).GetEnumerator().MoveNext();

                    var values = new OrderedDictionary<int, int> { [1] = 10, [2] = 20 };
                    IEnumerator enumerator = ((IEnumerable)values).GetEnumerator();
                    int score = 0;
                    try { _ = enumerator.Current; }
                    catch (InvalidOperationException) { score += 1; }
                    enumerator.MoveNext();
                    score += ((KeyValuePair<int, int>)enumerator.Current).Value;
                    enumerator.Reset();
                    enumerator.MoveNext();
                    score += ((KeyValuePair<int, int>)enumerator.Current).Key * 100;
                    values.Add(3, 30);
                    try { enumerator.MoveNext(); }
                    catch (InvalidOperationException) { score += 10000; }
                    return !emptyResult ? score : -1;
                }

                private static int ExerciseComparersAndLookup()
                {
                    var comparer = new AbsoluteComparer();
                    var values = new OrderedDictionary<int, string>(comparer)
                    {
                        [-2] = "two",
                        [3] = "three",
                    };
                    bool found = values.TryGetValue(2, out string? value, out int index);
                    bool missing = values.TryGetValue(4, out string? absent, out int missingIndex);
                    return ReferenceEquals(values.Comparer, comparer) && found && !missing
                        && value == "two" && absent is null && index == 0 && missingIndex == -1
                        && values.ContainsValue("three")
                        ? values.Count
                        : -1;
                }

                private static int ValidateFailures()
                {
                    int score = 0;
                    var values = new OrderedDictionary<int, int> { [1] = 10 };
                    try { values.Add(1, 20); }
                    catch (ArgumentException exception) { score += exception.ParamName == "key" ? 1 : 0; }
                    try { values.Insert(2, 2, 20); }
                    catch (ArgumentOutOfRangeException exception) { score += exception.ParamName == "index" ? 10 : 0; }
                    try { _ = values.GetAt(1); }
                    catch (ArgumentOutOfRangeException exception) { score += exception.ParamName == "index" ? 100 : 0; }

                    IDictionary dictionary = values;
                    try { dictionary.Add("bad", 1); }
                    catch (ArgumentException exception) { score += exception.ParamName == "key" ? 1000 : 0; }
                    try { dictionary.Add(2, "bad"); }
                    catch (ArgumentException exception) { score += exception.ParamName == "value" ? 10000 : 0; }
                    return score;
                }

                private static int Checksum(IEnumerable<KeyValuePair<int, int>> values)
                {
                    int result = 0;
                    foreach (KeyValuePair<int, int> value in values) result = result * 31 + value.Key * 3 + value.Value;
                    return result;
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
