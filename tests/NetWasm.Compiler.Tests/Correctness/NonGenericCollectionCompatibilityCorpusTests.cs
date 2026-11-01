namespace NetWasm.Compiler.Tests.Correctness;

public sealed class NonGenericCollectionCompatibilityCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void NonGenericCollectionsMatchDesktopAcrossCompilerProfilesAndTargets() =>
        runner.Run(new(
            "NonGenericCollectionCompatibilityCorpus",
            "NetWasm.Correctness.NonGenericCollections",
            """
            using System;
            using System.Collections;

            namespace NetWasm.Correctness.NonGenericCollections;

            public static class EntryPoint
            {
                public static int Run(int input) => input switch
                {
                    0 => ArrayListBasics(),
                    1 => ArrayListRange(),
                    2 => ArrayListInvalidatedRange(),
                    3 => ArrayListAdapterRangeEnumerator(),
                    4 => ArrayListFixedSize(),
                    5 => ArrayListReadOnly(),
                    6 => ArrayListAlgorithms(),
                    7 => ArrayListCopiesAndClone(),
                    8 => ArrayListEnumeratorClone(),
                    9 => ArrayListSynchronizedWrappers(),
                    10 => HashtableViewsAndEnumeration(),
                    11 => HashtableEnumeratorClone(),
                    12 => HashtableSynchronizedWrapper(),
                    13 => HashtableDictionaryConstructor(),
                    _ => ValidateFailures(),
                };

                private static int ArrayListBasics()
                {
                    var values = new ArrayList(1) { 1, 3 };
                    values.Insert(1, 2);
                    values.AddRange(new object[] { 4, 5 });
                    values.Remove(3);
                    values.RemoveAt(0);
                    values.InsertRange(0, new object[] { 7, 8 });
                    values.RemoveRange(1, 2);
                    return values.Count == 3 && (int)values[0]! == 7 && (int)values[1]! == 4 && (int)values[2]! == 5
                        ? Checksum(values)
                        : -1;
                }

                private static int ArrayListRange()
                {
                    var values = new ArrayList { 0, 1, 2, 3, 4, 5 };
                    ArrayList range = values.GetRange(1, 3);
                    range[1] = 20;
                    range.Add(30);
                    range.Insert(1, 10);
                    range.RemoveAt(0);
                    range.Reverse();
                    return range.Count == 4 && values.Count == 7
                        ? Checksum(values) * 31 + Checksum(range)
                        : -1;
                }

                private static int ArrayListInvalidatedRange()
                {
                    var values = new ArrayList { 1, 2, 3 };
                    ArrayList range = values.GetRange(0, 2);
                    values.Add(4);
                    try
                    {
                        _ = range.Count;
                        return -1;
                    }
                    catch (InvalidOperationException)
                    {
                        return 1;
                    }
                }

                private static int ArrayListAdapterRangeEnumerator()
                {
                    IList source = new ArrayList { 4, 5, 6, 7 };
                    ArrayList adapter = ArrayList.Adapter(source);
                    IEnumerator enumerator = adapter.GetEnumerator(1, 2);
                    int result = 0;
                    while (enumerator.MoveNext()) result = result * 10 + (int)enumerator.Current;
                    enumerator.Reset();
                    enumerator.MoveNext();
                    return result * 10 + (int)enumerator.Current;
                }

                private static int ArrayListFixedSize()
                {
                    var source = new ArrayList { 1, 2, 3 };
                    ArrayList values = ArrayList.FixedSize(source);
                    values[1] = 20;
                    values.Reverse();
                    int score = values.IsFixedSize && !values.IsReadOnly ? Checksum(values) : -1000;
                    try { values.Add(4); }
                    catch (NotSupportedException) { score += 1000; }
                    return score;
                }

                private static int ArrayListReadOnly()
                {
                    var source = new ArrayList { 2, 4, 6 };
                    ArrayList values = ArrayList.ReadOnly(source);
                    int score = values.IsReadOnly && values.IsFixedSize && values.Contains(4) ? values.Count : -100;
                    try { values[0] = 3; }
                    catch (NotSupportedException) { score += 100; }
                    try { values.RemoveAt(0); }
                    catch (NotSupportedException) { score += 1000; }
                    return score;
                }

                private static int ArrayListAlgorithms()
                {
                    ArrayList values = ArrayList.Repeat(3, 3);
                    values.Add(1);
                    values.Add(5);
                    values.Sort();
                    int found = values.BinarySearch(3);
                    int missing = values.BinarySearch(4);
                    values.Reverse(1, 3);
                    return found >= 0 && missing < 0 ? Checksum(values) + found * 100000 + missing : -1;
                }

                private static int ArrayListCopiesAndClone()
                {
                    var values = new ArrayList { 3, 6, 9 };
                    var copy = new object?[5];
                    values.CopyTo(copy, 1);
                    var clone = (ArrayList)values.Clone();
                    clone[0] = 30;
                    object?[] array = values.ToArray();
                    Array typed = values.ToArray(typeof(object));
                    return (int)copy[2]! == 6 && (int)array[2]! == 9 && (int)typed.GetValue(0)! == 3
                        && (int)values[0]! == 3 && (int)clone[0]! == 30
                        ? Checksum(values) * 100 + Checksum(clone)
                        : -1;
                }

                private static int ArrayListEnumeratorClone()
                {
                    var values = new ArrayList { 4, 5, 6 };
                    IEnumerator first = values.GetEnumerator();
                    first.MoveNext();
                    IEnumerator clone = (IEnumerator)((ICloneable)first).Clone();
                    first.MoveNext();
                    clone.MoveNext();
                    return (int)first.Current * 10 + (int)clone.Current;
                }

                private static int ArrayListSynchronizedWrappers()
                {
                    var source = new ArrayList { 1, 2 };
                    ArrayList values = ArrayList.Synchronized(source);
                    values.Add(3);
                    IList list = ArrayList.Synchronized((IList)new ArrayList { 4, 5 });
                    list.Add(6);
                    return values.IsSynchronized && list.IsSynchronized
                        ? Checksum(values) * 100 + Checksum(list)
                        : -1;
                }

                private static int HashtableViewsAndEnumeration()
                {
                    var values = new Hashtable { [3] = 30, [1] = 10, [2] = 20 };
                    var keys = new object?[5];
                    var items = new object?[5];
                    values.Keys.CopyTo(keys, 1);
                    values.Values.CopyTo(items, 1);
                    int sum = 0;
                    IDictionaryEnumerator enumerator = values.GetEnumerator();
                    while (enumerator.MoveNext()) sum += (int)enumerator.Key + (int)enumerator.Value!;
                    return sum + NonNullCount(keys) * 100 + NonNullCount(items) * 1000;
                }

                private static int HashtableEnumeratorClone()
                {
                    var values = new Hashtable { [1] = 10, [2] = 20, [3] = 30 };
                    IDictionaryEnumerator first = values.GetEnumerator();
                    first.MoveNext();
                    var clone = (IDictionaryEnumerator)((ICloneable)first).Clone();
                    int current = (int)first.Key + (int)clone.Key;
                    bool movedFirst = first.MoveNext();
                    bool movedClone = clone.MoveNext();
                    return movedFirst && movedClone && (int)first.Key == (int)clone.Key
                        ? current * 100 + (int)first.Key
                        : -1;
                }

                private static int HashtableSynchronizedWrapper()
                {
                    Hashtable values = Hashtable.Synchronized(new Hashtable());
                    values.Add(1, 10);
                    values[2] = 20;
                    Hashtable clone = (Hashtable)values.Clone();
                    clone.Remove(1);
                    return values.IsSynchronized && clone.IsSynchronized && values.Count == 2 && clone.Count == 1
                        && (int)values[2]! == 20
                        ? 21
                        : -1;
                }

                private static int HashtableDictionaryConstructor()
                {
                    IDictionary source = new Hashtable { [1] = 10, [2] = 20 };
                    var values = new Hashtable(source, 0.5f);
                    values[3] = 30;
                    return values.Count == 3 && values.ContainsKey(2) && values.ContainsValue(30)
                        ? (int)values[1]! + (int)values[2]! + (int)values[3]!
                        : -1;
                }

                private static int ValidateFailures()
                {
                    int score = 0;
                    try { _ = new ArrayList(-1); }
                    catch (ArgumentOutOfRangeException exception) { score += exception.ParamName == "capacity" ? 1 : 0; }
                    try { ArrayList.Repeat(1, -1); }
                    catch (ArgumentOutOfRangeException exception) { score += exception.ParamName == "count" ? 10 : 0; }
                    try { _ = new Hashtable(1, 0.05f); }
                    catch (ArgumentOutOfRangeException exception) { score += exception.ParamName == "loadFactor" ? 100 : 0; }
                    var values = new Hashtable { [1] = 10 };
                    try { values.Add(1, 20); }
                    catch (ArgumentException) { score += 1000; }
                    IDictionaryEnumerator enumerator = values.GetEnumerator();
                    values[2] = 20;
                    try { enumerator.MoveNext(); }
                    catch (InvalidOperationException) { score += 10000; }
                    return score;
                }

                private static int Checksum(IEnumerable values)
                {
                    int result = 0;
                    foreach (object? value in values) result = unchecked(result * 31 + (int)value!);
                    return result;
                }

                private static int NonNullCount(object?[] values)
                {
                    int count = 0;
                    foreach (object? value in values) if (value != null) count++;
                    return count;
                }

                public static int Trace() => 0;
            }
            """,
            [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14])
        {
            CaptureCompilerDiagnostics = true,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });
}
