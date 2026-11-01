namespace NetWasm.Compiler.Tests.Correctness;

public sealed class HashSetCompatibilityCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void HashSetsMatchDesktopAcrossCompilerProfilesAndTargets() =>
        runner.Run(new(
            "HashSetCompatibilityCorpus",
            "NetWasm.Correctness.HashSets",
            """
            using System;
            using System.Collections.Generic;

            namespace NetWasm.Correctness.HashSets;

            public sealed class ConstantHashComparer : IEqualityComparer<int>
            {
                public bool Equals(int left, int right) => left == right;
                public int GetHashCode(int value) => 1;
            }

            public sealed class Payload
            {
                public Payload(int key, int value)
                {
                    Key = key;
                    Value = value;
                }

                public int Key { get; }
                public int Value { get; }
            }

            public sealed class PayloadComparer : IEqualityComparer<Payload>
            {
                public bool Equals(Payload? left, Payload? right) => left?.Key == right?.Key;
                public int GetHashCode(Payload value) => value.Key;
            }

            public static class EntryPoint
            {
                public static int Run(int input) => input switch
                {
                    0 => ConstructAndCopy(),
                    1 => AddRemoveAndTrim(),
                    2 => UnionAndIntersect(),
                    3 => ExceptAndSymmetricExcept(),
                    4 => CheckRelations(),
                    5 => ExerciseCollisionsAndFreeList(),
                    6 => RemoveWhere(),
                    7 => RecoverCanonicalValue(),
                    8 => UseAlternateLookup(),
                    9 => CompareSets(),
                    10 => PreserveEnumeratorAcrossRemoval(),
                    11 => InvalidateEnumeratorOnAddition(),
                    12 => ExerciseLargeBitHelperPath(),
                    _ => ValidateArguments(),
                };

                private static int ConstructAndCopy()
                {
                    var source = new[] { 1, 2, 2, 3 };
                    var values = new HashSet<int>(source);
                    var copy = new HashSet<int>(values, values.Comparer);
                    var array = new int[5];
                    copy.CopyTo(array, 1, copy.Count);
                    return copy.Count * 10000 + copy.Capacity * 100 + Sum(array);
                }

                private static int AddRemoveAndTrim()
                {
                    var values = new HashSet<int>(2);
                    for (int value = 0; value < 20; value++) values.Add(value);
                    for (int value = 0; value < 20; value += 2) values.Remove(value);
                    int before = values.Capacity;
                    values.TrimExcess();
                    int after = values.Capacity;
                    int ensured = values.EnsureCapacity(40);
                    return values.Count * 1000000 + before * 10000 + after * 100 + ensured;
                }

                private static int UnionAndIntersect()
                {
                    var values = new HashSet<int> { 1, 2, 3 };
                    values.UnionWith(new[] { 3, 4, 5 });
                    values.IntersectWith(new[] { 2, 4, 4, 6 });
                    return values.Count * 100 + Sum(values);
                }

                private static int ExceptAndSymmetricExcept()
                {
                    var values = new HashSet<int> { 1, 2, 3, 4 };
                    values.ExceptWith(new[] { 2, 8 });
                    values.SymmetricExceptWith(new[] { 3, 5, 5, 6 });
                    return values.Count * 1000 + Sum(values);
                }

                private static int CheckRelations()
                {
                    var values = new HashSet<int> { 1, 2, 3 };
                    int result = 0;
                    if (values.IsSubsetOf(new[] { 0, 1, 2, 3, 4 })) result |= 1;
                    if (values.IsProperSubsetOf(new[] { 1, 2, 3, 4 })) result |= 2;
                    if (values.IsSupersetOf(new[] { 1, 3 })) result |= 4;
                    if (values.IsProperSupersetOf(new[] { 2 })) result |= 8;
                    if (values.Overlaps(new[] { 9, 3 })) result |= 16;
                    if (values.SetEquals(new[] { 3, 2, 1, 1 })) result |= 32;
                    return result;
                }

                private static int ExerciseCollisionsAndFreeList()
                {
                    var values = new HashSet<int>(1, new ConstantHashComparer());
                    for (int value = 0; value < 48; value++) values.Add(value);
                    for (int value = 0; value < 48; value += 3) values.Remove(value);
                    for (int value = 100; value < 116; value++) values.Add(value);
                    return values.Count * 10000
                        + (values.Contains(47) ? 1000 : 0)
                        + (values.Contains(45) ? 100 : 0)
                        + (values.Contains(115) ? 10 : 0)
                        + (values.Add(115) ? 1 : 0);
                }

                private static int RemoveWhere()
                {
                    var values = new HashSet<int> { 1, 2, 3, 4, 5, 6 };
                    int removed = values.RemoveWhere(static value => (value & 1) == 0);
                    return removed * 100 + Sum(values);
                }

                private static int RecoverCanonicalValue()
                {
                    var stored = new Payload(7, 42);
                    var values = new HashSet<Payload>(new PayloadComparer()) { stored };
                    bool found = values.TryGetValue(new Payload(7, 0), out var actual);
                    return found && ReferenceEquals(stored, actual) ? actual.Value : -1;
                }

                private static int UseAlternateLookup()
                {
                    var values = new HashSet<string>(StringComparer.Ordinal) { "alpha", "beta" };
                    var lookup = values.GetAlternateLookup<ReadOnlySpan<char>>();
                    bool found = lookup.TryGetValue("alpha".AsSpan(), out var actual);
                    bool added = lookup.Add("gamma".AsSpan());
                    bool removed = lookup.Remove("beta".AsSpan());
                    return found && actual == "alpha" && added && removed
                        && lookup.Contains("gamma".AsSpan())
                        ? values.Count * 100 + actual.Length
                        : -1;
                }

                private static int CompareSets()
                {
                    var comparer = HashSet<int>.CreateSetComparer();
                    var left = new HashSet<int> { 1, 2, 3 };
                    var right = new HashSet<int> { 3, 2, 1 };
                    var different = new HashSet<int> { 1, 2, 4 };
                    return comparer.Equals(left, right) && !comparer.Equals(left, different)
                        && comparer.GetHashCode(left) == comparer.GetHashCode(right)
                        ? 1
                        : 0;
                }

                private static int PreserveEnumeratorAcrossRemoval()
                {
                    var values = new HashSet<int> { 1, 2, 3 };
                    var enumerator = values.GetEnumerator();
                    enumerator.MoveNext();
                    values.Remove(2);
                    int count = 1;
                    while (enumerator.MoveNext()) count++;
                    values.Clear();
                    return count * 10 + values.Count;
                }

                private static int InvalidateEnumeratorOnAddition()
                {
                    var values = new HashSet<int> { 1, 2 };
                    var enumerator = values.GetEnumerator();
                    enumerator.MoveNext();
                    values.Add(3);
                    try
                    {
                        enumerator.MoveNext();
                        return -1;
                    }
                    catch (InvalidOperationException)
                    {
                        return values.Count;
                    }
                }

                private static int ExerciseLargeBitHelperPath()
                {
                    var values = new HashSet<int>();
                    var other = new List<int>();
                    for (int value = 0; value < 3400; value++)
                    {
                        values.Add(value);
                        if ((value & 1) == 0) other.Add(value);
                    }
                    values.IntersectWith(other);
                    return values.Count * 10000 + (values.Contains(3398) ? 1 : 0);
                }

                private static int ValidateArguments()
                {
                    var values = new HashSet<int>();
                    int score = 0;
                    try { values.UnionWith(null!); }
                    catch (ArgumentNullException exception) { score += exception.ParamName == "other" ? 1 : 0; }
                    try { values.EnsureCapacity(-1); }
                    catch (ArgumentOutOfRangeException exception) { score += exception.ParamName == "capacity" ? 10 : 0; }
                    try { values.TrimExcess(-1); }
                    catch (ArgumentOutOfRangeException exception) { score += exception.ParamName == "capacity" ? 100 : 0; }
                    return score;
                }

                private static int Sum(IEnumerable<int> values)
                {
                    int result = 0;
                    foreach (int value in values) result += value;
                    return result;
                }

                public static int Trace() => 0;
            }
            """,
            [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13])
        {
            CaptureCompilerDiagnostics = true,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });
}
