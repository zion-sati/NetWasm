namespace NetWasm.Compiler.Tests.Correctness;

public sealed class ConcurrentDictionaryCompatibilityCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void ConcurrentDictionaryMatchesDesktopAcrossCompilerProfilesAndTargets() =>
        runner.Run(new(
            "ConcurrentDictionaryCompatibilityCorpus",
            "NetWasm.Correctness.ConcurrentDictionaries",
            """
            using System;
            using System.Collections;
            using System.Collections.Concurrent;
            using System.Collections.Generic;

            namespace NetWasm.Correctness.ConcurrentDictionaries;

            public static class EntryPoint
            {
                public static int Run(int input) => input switch
                {
                    0 => ConstructorsAndComparer(),
                    1 => BasicOperations(),
                    2 => Factories(),
                    3 => StatefulFactories(),
                    4 => ReentrantFactories(),
                    5 => PairRemoval(),
                    6 => Snapshots(),
                    7 => GenericInterfaces(),
                    8 => NonGenericInterfaces(),
                    9 => Copies(),
                    10 => Enumeration(),
                    _ => AlternateLookup(),
                };

                private static int ConstructorsAndComparer()
                {
                    var defaultComparer = new ConcurrentDictionary<string, int>(
                        (IEqualityComparer<string>?)null);
                    var custom = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    var defaultConcurrency = new ConcurrentDictionary<int, int>(-1, 0);
                    int score = ReferenceEquals(defaultComparer.Comparer, EqualityComparer<string>.Default) ? 1 : 0;
                    score += ReferenceEquals(custom.Comparer, StringComparer.OrdinalIgnoreCase) ? 10 : 0;
                    score += defaultConcurrency.IsEmpty ? 100 : 0;

                    try { _ = new ConcurrentDictionary<int, int>(0, 0); }
                    catch (ArgumentOutOfRangeException exception) { score += exception.ParamName == "concurrencyLevel" ? 1000 : 0; }
                    try { _ = new ConcurrentDictionary<int, int>(1, -1); }
                    catch (ArgumentOutOfRangeException exception) { score += exception.ParamName == "capacity" ? 10000 : 0; }
                    try
                    {
                        _ = new ConcurrentDictionary<int, int>(
                            new KeyValuePair<int, int>[] { new(1, 10), new(1, 20) });
                    }
                    catch (ArgumentException) { score += 100000; }
                    return score;
                }

                private static int BasicOperations()
                {
                    var dictionary = new ConcurrentDictionary<int, int>();
                    bool first = dictionary.TryAdd(1, 10);
                    bool duplicate = dictionary.TryAdd(1, 20);
                    dictionary[2] = 20;
                    bool updated = dictionary.TryUpdate(2, 21, 20);
                    bool rejected = dictionary.TryUpdate(2, 22, 20);
                    bool removed = dictionary.TryRemove(1, out int removedValue);
                    return first && !duplicate && updated && !rejected && removed && removedValue == 10
                        && dictionary.Count == 1 && !dictionary.IsEmpty && dictionary.ContainsKey(2)
                        && dictionary.TryGetValue(2, out int value)
                        ? value
                        : -1;
                }

                private static int Factories()
                {
                    var dictionary = new ConcurrentDictionary<int, int>();
                    int addCalls = 0;
                    int updateCalls = 0;
                    int first = dictionary.GetOrAdd(1, key => { addCalls++; return key * 10; });
                    int second = dictionary.GetOrAdd(1, key => { addCalls++; return key * 100; });
                    int third = dictionary.AddOrUpdate(
                        2,
                        key => { addCalls++; return key * 10; },
                        (key, old) => { updateCalls++; return old + key; });
                    int fourth = dictionary.AddOrUpdate(
                        2,
                        100,
                        (key, old) => { updateCalls++; return old + key; });
                    return first + second + third + fourth + addCalls * 100 + updateCalls * 1000;
                }

                private static int StatefulFactories()
                {
                    var dictionary = new ConcurrentDictionary<int, int>();
                    int first = dictionary.GetOrAdd(2, static (key, arg) => key + arg, 30);
                    int second = dictionary.AddOrUpdate(
                        3,
                        static (key, arg) => key * arg,
                        static (key, old, arg) => old + key + arg,
                        4);
                    int third = dictionary.AddOrUpdate(
                        3,
                        static (key, arg) => key * arg,
                        static (key, old, arg) => old + key + arg,
                        5);
                    ReadOnlySpan<char> abc = "abc";
                    int fourth = dictionary.GetOrAdd(
                        4,
                        static (key, arg) => key + arg.Length,
                        abc);
                    ReadOnlySpan<char> xy = "xy";
                    int fifth = dictionary.AddOrUpdate(
                        5,
                        static (key, arg) => key * arg.Length,
                        static (key, old, arg) => old + key + arg.Length,
                        xy);
                    ReadOnlySpan<char> xyz = "xyz";
                    int sixth = dictionary.AddOrUpdate(
                        5,
                        static (key, arg) => key * arg.Length,
                        static (key, old, arg) => old + key + arg.Length,
                        xyz);
                    int result = first;
                    result = unchecked(result * 31 + second);
                    result = unchecked(result * 31 + third);
                    result = unchecked(result * 31 + fourth);
                    result = unchecked(result * 31 + fifth);
                    return unchecked(result * 31 + sixth);
                }

                private static int ReentrantFactories()
                {
                    var dictionary = new ConcurrentDictionary<int, int>();
                    dictionary[1] = 10;
                    int updateCalls = 0;
                    int updated = dictionary.AddOrUpdate(
                        1,
                        0,
                        (key, old) =>
                        {
                            updateCalls++;
                            dictionary[key] = 20;
                            return old + 1;
                        });

                    int addCalls = 0;
                    int existing = dictionary.GetOrAdd(
                        2,
                        key =>
                        {
                            addCalls++;
                            dictionary[key] = 30;
                            return 40;
                        });
                    return updated * 1000 + existing * 10 + updateCalls + addCalls;
                }

                private static int PairRemoval()
                {
                    var dictionary = new ConcurrentDictionary<int, string>();
                    dictionary[1] = "one";
                    bool wrong = dictionary.TryRemove(new KeyValuePair<int, string>(1, "two"));
                    bool right = dictionary.TryRemove(new KeyValuePair<int, string>(1, "one"));
                    return !wrong && right && dictionary.IsEmpty ? 1 : -1;
                }

                private static int Snapshots()
                {
                    var dictionary = new ConcurrentDictionary<int, int>();
                    dictionary[1] = 10;
                    dictionary[2] = 20;
                    ICollection<int> keys = dictionary.Keys;
                    ICollection<int> values = dictionary.Values;
                    dictionary[3] = 30;
                    int keySum = 0;
                    int valueSum = 0;
                    foreach (int key in keys) keySum += key;
                    foreach (int value in values) valueSum += value;
                    return keys.Count * 10000 + values.Count * 1000 + keySum * 100 + valueSum;
                }

                private static int GenericInterfaces()
                {
                    var dictionary = new ConcurrentDictionary<int, int>();
                    IDictionary<int, int> map = dictionary;
                    ICollection<KeyValuePair<int, int>> pairs = dictionary;
                    map.Add(1, 10);
                    pairs.Add(new KeyValuePair<int, int>(2, 20));
                    bool contains = pairs.Contains(new KeyValuePair<int, int>(2, 20));
                    bool wrong = pairs.Remove(new KeyValuePair<int, int>(2, 21));
                    bool right = pairs.Remove(new KeyValuePair<int, int>(2, 20));
                    int score = contains && !wrong && right && !pairs.IsReadOnly ? 1 : 0;
                    try { map.Add(1, 11); }
                    catch (ArgumentException) { score += 10; }
                    return score;
                }

                private static int NonGenericInterfaces()
                {
                    IDictionary map = new ConcurrentDictionary<int, int>();
                    ICollection collection = (ICollection)map;
                    map.Add(1, 10);
                    int score = map.Contains(1) && !map.Contains("1") && (int)map[1]! == 10
                        && map["1"] is null && !map.IsFixedSize && !map.IsReadOnly
                        && !collection.IsSynchronized
                        ? 1
                        : 0;
                    try { _ = collection.SyncRoot; }
                    catch (NotSupportedException) { score += 10; }
                    try { map["bad"] = 1; }
                    catch (ArgumentException) { score += 100; }
                    try { map[2] = "bad"; }
                    catch (ArgumentException) { score += 1000; }
                    map.Remove("bad");
                    map.Remove(1);
                    return score + (map.Count == 0 ? 10000 : 0);
                }

                private static int Copies()
                {
                    var dictionary = new ConcurrentDictionary<int, int>();
                    dictionary[1] = 10;
                    dictionary[2] = 20;
                    ICollection collection = dictionary;
                    var pairs = new KeyValuePair<int, int>[3];
                    var entries = new DictionaryEntry[3];
                    var objects = new object[3];
                    collection.CopyTo(pairs, 1);
                    collection.CopyTo(entries, 1);
                    collection.CopyTo(objects, 1);
                    int pairScore = 0;
                    int entryScore = 0;
                    int objectScore = 0;
                    for (int index = 1; index < 3; index++)
                    {
                        pairScore += pairs[index].Key * 10 + pairs[index].Value;
                        entryScore += (int)entries[index].Key * 10 + (int)entries[index].Value!;
                        var pair = (KeyValuePair<int, int>)objects[index];
                        objectScore += pair.Key * 10 + pair.Value;
                    }
                    int failures = 0;
                    try { collection.CopyTo(new int[2], 0); }
                    catch (ArgumentException exception) { failures += exception.ParamName == "array" ? 1 : 0; }
                    try { collection.CopyTo(new object[1], 0); }
                    catch (ArgumentException) { failures += 10; }
                    return pairScore * 10000 + entryScore * 100 + objectScore + failures;
                }

                private static int Enumeration()
                {
                    var dictionary = new ConcurrentDictionary<int, int>();
                    dictionary[1] = 10;
                    dictionary[2] = 20;
                    int genericSum = 0;
                    foreach (KeyValuePair<int, int> pair in dictionary)
                        genericSum += pair.Key * 10 + pair.Value;
                    IDictionaryEnumerator enumerator = ((IDictionary)dictionary).GetEnumerator();
                    int nonGenericSum = 0;
                    while (enumerator.MoveNext())
                        nonGenericSum += (int)enumerator.Key * 10 + (int)enumerator.Value!;
                    return genericSum * 100 + nonGenericSum;
                }

                private static int AlternateLookup()
                {
                    var dictionary = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
                    bool compatible = dictionary.TryGetAlternateLookup<ReadOnlySpan<char>>(out var lookup);
                    ReadOnlySpan<char> alpha = "alpha";
                    bool added = lookup.TryAdd(alpha, 10);
                    bool found = lookup.TryGetValue(alpha, out string? actualKey, out int value);
                    lookup[alpha] = 20;
                    bool removed = lookup.TryRemove(alpha, out string? removedKey, out int removedValue);
                    bool incompatible = dictionary.TryGetAlternateLookup<int>(out _);
                    int score = compatible && added && found && actualKey == "alpha" && value == 10
                        && removed && removedKey == "alpha" && removedValue == 20
                        && ReferenceEquals(lookup.Dictionary, dictionary) && !incompatible
                        ? 1
                        : 0;
                    try { _ = dictionary.GetAlternateLookup<int>(); }
                    catch (InvalidOperationException) { score += 10; }
                    return score;
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
