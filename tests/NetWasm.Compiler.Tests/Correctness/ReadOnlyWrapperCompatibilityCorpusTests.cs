namespace NetWasm.Compiler.Tests.Correctness;

public sealed class ReadOnlyWrapperCompatibilityCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void ReadOnlyWrappersMatchDesktopAcrossCompilerProfilesAndTargets() =>
        runner.Run(new(
            "ReadOnlyWrapperCompatibilityCorpus",
            "NetWasm.Correctness.ReadOnlyWrappers",
            """
            using System;
            using System.Collections;
            using System.Collections.Generic;
            using System.Collections.ObjectModel;

            namespace NetWasm.Correctness.ReadOnlyWrappers;

            public sealed class GenericOnlyDictionary<TKey, TValue> : IDictionary<TKey, TValue>
                where TKey : notnull
            {
                private readonly Dictionary<TKey, TValue> _inner = new();
                public TValue this[TKey key] { get => _inner[key]; set => _inner[key] = value; }
                public ICollection<TKey> Keys => _inner.Keys;
                public ICollection<TValue> Values => _inner.Values;
                public int Count => _inner.Count;
                public bool IsReadOnly => false;
                public void Add(TKey key, TValue value) => _inner.Add(key, value);
                public void Add(KeyValuePair<TKey, TValue> item) => _inner.Add(item.Key, item.Value);
                public void Clear() => _inner.Clear();
                public bool Contains(KeyValuePair<TKey, TValue> item) =>
                    ((ICollection<KeyValuePair<TKey, TValue>>)_inner).Contains(item);
                public bool ContainsKey(TKey key) => _inner.ContainsKey(key);
                public void CopyTo(KeyValuePair<TKey, TValue>[] array, int index) =>
                    ((ICollection<KeyValuePair<TKey, TValue>>)_inner).CopyTo(array, index);
                public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => _inner.GetEnumerator();
                public bool Remove(TKey key) => _inner.Remove(key);
                public bool Remove(KeyValuePair<TKey, TValue> item) =>
                    ((ICollection<KeyValuePair<TKey, TValue>>)_inner).Remove(item);
                public bool TryGetValue(TKey key, out TValue value) => _inner.TryGetValue(key, out value!);
                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            }

            public static class EntryPoint
            {
                public static int Run(int input) => input switch
                {
                    0 => DictionaryBasics(),
                    1 => DictionaryBackingChanges(),
                    2 => DictionaryMutations(),
                    3 => NonGenericDictionary(),
                    4 => DictionaryCopies(),
                    5 => DictionaryViews(),
                    6 => GenericOnlyEnumeration(),
                    7 => SetBasics(),
                    8 => SetMutations(),
                    9 => SetCopyAndSync(),
                    10 => SetBuilder(),
                    _ => ValidateFailures(),
                };

                private static int DictionaryBasics()
                {
                    var source = new Dictionary<int, string> { [2] = "two", [1] = "one" };
                    var values = new ReadOnlyDictionary<int, string>(source);
                    bool found = values.TryGetValue(1, out string? value);
                    return found && value == "one" && values.ContainsKey(2)
                        && values[2] == "two" && values.Count == 2
                        ? values.Keys.Count * 100 + values.Values.Count
                        : -1;
                }

                private static int DictionaryBackingChanges()
                {
                    var source = new Dictionary<int, int> { [1] = 10 };
                    var values = new ReadOnlyDictionary<int, int>(source);
                    var keys = values.Keys;
                    ICollection<int> items = values.Values;
                    source[2] = 20;
                    return keys.Contains(2) && items.Contains(20) && values.Count == 2
                        ? Checksum(keys) * 100 + Checksum(items)
                        : -1;
                }

                private static int DictionaryMutations()
                {
                    int score = 0;
                    var values = new ReadOnlyDictionary<int, int>(new Dictionary<int, int> { [1] = 10 });
                    IDictionary<int, int> dictionary = values;
                    ICollection<KeyValuePair<int, int>> collection = values;
                    try { dictionary.Add(2, 20); } catch (NotSupportedException) { score += 1; }
                    try { dictionary.Remove(1); } catch (NotSupportedException) { score += 10; }
                    try { dictionary[1] = 20; } catch (NotSupportedException) { score += 100; }
                    try { collection.Clear(); } catch (NotSupportedException) { score += 1000; }
                    try { collection.Remove(new(1, 10)); } catch (NotSupportedException) { score += 10000; }
                    return score;
                }

                private static int NonGenericDictionary()
                {
                    IDictionary values = new ReadOnlyDictionary<int, string>(
                        new Dictionary<int, string> { [2] = "two", [1] = "one" });
                    IDictionaryEnumerator enumerator = values.GetEnumerator();
                    enumerator.MoveNext();
                    DictionaryEntry entry = enumerator.Entry;
                    return values.IsReadOnly && values.IsFixedSize && values.Contains(2)
                        && !values.Contains("bad") && (string)values[1]! == "one"
                        && (int)entry.Key == 2 && (string)entry.Value! == "two"
                        ? values.Count
                        : -1;
                }

                private static int DictionaryCopies()
                {
                    ICollection values = new ReadOnlyDictionary<int, string>(
                        new Dictionary<int, string> { [2] = "two", [1] = "one" });
                    var pairs = new KeyValuePair<int, string>[3];
                    var entries = new DictionaryEntry[3];
                    var objects = new object?[3];
                    values.CopyTo(pairs, 1);
                    values.CopyTo(entries, 1);
                    values.CopyTo(objects, 1);
                    return pairs[1].Key == 2 && (int)entries[2].Key == 1
                        && ((KeyValuePair<int, string>)objects[1]!).Value == "two"
                        ? pairs.Length * 100 + entries.Length * 10 + objects.Length
                        : -1;
                }

                private static int DictionaryViews()
                {
                    var source = new Dictionary<int, int> { [1] = 10, [2] = 20 };
                    var values = new ReadOnlyDictionary<int, int>(source);
                    ICollection keys = values.Keys;
                    ICollection items = values.Values;
                    var keyCopy = new object?[3];
                    var valueCopy = new object?[3];
                    keys.CopyTo(keyCopy, 1);
                    items.CopyTo(valueCopy, 1);
                    object sync = ((ICollection)source).SyncRoot;
                    return ReferenceEquals(keys.SyncRoot, sync) && ReferenceEquals(items.SyncRoot, sync)
                        && (int)keyCopy[1]! == 1 && (int)valueCopy[2]! == 20
                        ? keys.Count * 100 + items.Count
                        : -1;
                }

                private static int GenericOnlyEnumeration()
                {
                    var source = new GenericOnlyDictionary<int, int> { [3] = 30, [4] = 40 };
                    IDictionary values = new ReadOnlyDictionary<int, int>(source);
                    IDictionaryEnumerator enumerator = values.GetEnumerator();
                    int result = 0;
                    while (enumerator.MoveNext()) result = result * 31 + (int)enumerator.Key + (int)enumerator.Value!;
                    enumerator.Reset();
                    return result;
                }

                private static int SetBasics()
                {
                    var source = new HashSet<int> { 1, 2, 3 };
                    var values = new ReadOnlySet<int>(source);
                    source.Add(4);
                    return values.Contains(4) && values.IsSubsetOf(new[] { 1, 2, 3, 4, 5 })
                        && values.IsProperSupersetOf(new[] { 1, 2 }) && values.Overlaps(new[] { 4, 8 })
                        && values.SetEquals(new[] { 4, 3, 2, 1 })
                        ? values.Count
                        : -1;
                }

                private static int SetMutations()
                {
                    int score = 0;
                    ISet<int> values = new ReadOnlySet<int>(new HashSet<int> { 1 });
                    try { values.Add(2); } catch (NotSupportedException) { score += 1; }
                    try { values.ExceptWith(new[] { 1 }); } catch (NotSupportedException) { score += 10; }
                    try { values.IntersectWith(new[] { 1 }); } catch (NotSupportedException) { score += 100; }
                    try { values.SymmetricExceptWith(new[] { 2 }); } catch (NotSupportedException) { score += 1000; }
                    try { values.UnionWith(new[] { 2 }); } catch (NotSupportedException) { score += 10000; }
                    try { values.Clear(); } catch (NotSupportedException) { score += 100000; }
                    return score;
                }

                private static int SetCopyAndSync()
                {
                    var source = new HashSet<string> { "a", "b" };
                    var wrapper = new ReadOnlySet<string>(source);
                    ICollection values = wrapper;
                    var copy = new object?[3];
                    values.CopyTo(copy, 1);
                    return !values.IsSynchronized && ReferenceEquals(values.SyncRoot, wrapper)
                        && (string)copy[1]! == "a" && (string)copy[2]! == "b"
                        ? values.Count
                        : -1;
                }

                private static int SetBuilder()
                {
                    ReadOnlySet<int> values = [3, 1, 3, 2];
                    return values.Count == 3 && values.SetEquals(new[] { 1, 2, 3 }) ? Checksum(values) : -1;
                }

                private static int ValidateFailures()
                {
                    int score = 0;
                    try { _ = new ReadOnlyDictionary<int, int>(null!); }
                    catch (ArgumentNullException exception) { score += exception.ParamName == "dictionary" ? 1 : 0; }
                    IDictionary values = new ReadOnlyDictionary<int, int>(new Dictionary<int, int>());
                    try { values.Contains(null!); }
                    catch (ArgumentNullException exception) { score += exception.ParamName == "key" ? 10 : 0; }
                    try { ((ICollection)values).CopyTo(null!, 0); }
                    catch (ArgumentNullException exception) { score += exception.ParamName == "array" ? 100 : 0; }
                    try { _ = new ReadOnlySet<int>(null!); }
                    catch (ArgumentNullException exception) { score += exception.ParamName == "set" ? 1000 : 0; }
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
            [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11])
        {
            CaptureCompilerDiagnostics = true,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });
}
