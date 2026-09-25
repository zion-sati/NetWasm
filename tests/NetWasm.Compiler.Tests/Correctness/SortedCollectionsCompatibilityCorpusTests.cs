using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

[Collection(CorrectnessTestGroup.Name)]
public sealed class SortedCollectionsCompatibilityCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void SortedCollectionsMatchDesktopForTreeViewsComparersAndInterfaces() =>
        runner.Run(new(
            "SortedCollectionsCompatibilityCorpus",
            "NetWasm.Correctness.SortedCollections",
            """
            using System;
            using System.Collections;
            using System.Collections.Generic;

            namespace NetWasm.Correctness.SortedCollections;

            public sealed class DescendingComparer : IComparer<int>
            {
                public int Compare(int left, int right) => right.CompareTo(left);
            }

            public sealed class ThrowingComparer : IComparer<int>
            {
                public int Compare(int left, int right) => throw new InvalidOperationException();
            }

            public sealed class Payload
            {
                public Payload(int value) => Value = value;
                public int Value { get; }
            }

            public static class EntryPoint
            {
                public static int Run(int input) => input switch
                {
                    0 => ExerciseRotationsAndRemovals(),
                    1 => ExerciseCustomComparerAndOrdering(),
                    2 => ExerciseThrowingComparer(),
                    3 => ExerciseDuplicateKey(),
                    4 => ExerciseMissingKey(),
                    5 => ExerciseRangeViewAndBounds(),
                    6 => ExerciseVersionInvalidation(),
                    7 => ExerciseSetAlgebra(),
                    8 => ExerciseBoxedInterfaces(),
                    9 => ExerciseStructEnumerators(),
                    _ => ExerciseValueReferenceAndNullableValues(),
                };

                private static int ExerciseRotationsAndRemovals()
                {
                    var values = new SortedSet<int>();
                    foreach (var value in new[] { 5, 3, 7, 2, 4, 6, 8, 1, 9 })
                    {
                        values.Add(value);
                    }
                    values.Remove(5);
                    values.Remove(1);
                    values.Remove(9);
                    var checksum = 0;
                    foreach (var value in values)
                    {
                        checksum = checksum * 10 + value;
                    }
                    return values.Count * 100000 + checksum;
                }

                private static int ExerciseCustomComparerAndOrdering()
                {
                    var values = new SortedSet<int>(new DescendingComparer()) { 1, 4, 2, 3 };
                    var checksum = 0;
                    foreach (var value in values)
                    {
                        checksum = checksum * 10 + value;
                    }
                    return values.Min * 10000 + values.Max * 1000 + checksum;
                }

                private static int ExerciseThrowingComparer()
                {
                    var values = new SortedSet<int>(new ThrowingComparer());
                    values.Add(1);
                    values.Add(2);
                    return -1;
                }

                private static int ExerciseDuplicateKey()
                {
                    var values = new SortedDictionary<int, string>();
                    values.Add(4, "first");
                    values.Add(4, "second");
                    return -1;
                }

                private static int ExerciseMissingKey()
                {
                    var values = new SortedDictionary<int, string>();
                    return values[4].Length;
                }

                private static int ExerciseRangeViewAndBounds()
                {
                    var values = new SortedSet<int>();
                    for (var value = 1; value <= 9; value++)
                    {
                        values.Add(value);
                    }
                    var view = values.GetViewBetween(3, 7);
                    view.Remove(5);
                    view.Add(6);
                    return values.Count * 100000 + view.Count * 1000 + view.Min * 100 + view.Max * 10 +
                        (values.Contains(5) ? 1 : 0);
                }

                private static int ExerciseVersionInvalidation()
                {
                    var values = new SortedSet<int> { 1, 2, 3 };
                    var enumerator = values.GetEnumerator();
                    _ = enumerator.MoveNext();
                    values.Add(4);
                    try
                    {
                        _ = enumerator.MoveNext();
                        return -1;
                    }
                    catch (InvalidOperationException)
                    {
                        return 1;
                    }
                }

                private static int ExerciseSetAlgebra()
                {
                    var values = new SortedSet<int> { 1, 2, 3, 4 };
                    values.UnionWith(new[] { 4, 5, 6 });
                    values.IntersectWith(new[] { 2, 3, 5, 8 });
                    values.SymmetricExceptWith(new[] { 1, 3, 7 });
                    values.ExceptWith(new[] { 7 });
                    var checksum = 0;
                    foreach (var value in values)
                    {
                        checksum = checksum * 10 + value;
                    }
                    return values.Count * 100 + checksum;
                }

                private static int ExerciseBoxedInterfaces()
                {
                    var values = new SortedDictionary<int, string> { [2] = "two", [1] = "one" };
                    var dictionary = (IDictionary)values;
                    var enumerator = dictionary.GetEnumerator();
                    _ = enumerator.MoveNext();
                    var first = enumerator.Entry;
                    var contains = dictionary.Contains(2);
                    var keys = 0;
                    foreach (var key in (ICollection)values.Keys)
                    {
                        keys += (int)key!;
                    }
                    return (contains ? 10000 : 0) + (int)first.Key * 100 + keys;
                }

                private static int ExerciseStructEnumerators()
                {
                    var values = new SortedSet<int> { 4, 1, 3, 2 };
                    var setEnumerator = values.GetEnumerator();
                    var setChecksum = 0;
                    while (setEnumerator.MoveNext())
                    {
                        setChecksum = setChecksum * 10 + setEnumerator.Current;
                    }

                    var dictionary = new SortedDictionary<int, int> { [2] = 20, [1] = 10 };
                    var dictionaryEnumerator = dictionary.GetEnumerator();
                    var dictionaryChecksum = 0;
                    while (dictionaryEnumerator.MoveNext())
                    {
                        dictionaryChecksum += dictionaryEnumerator.Current.Value;
                    }
                    return setChecksum * 100 + dictionaryChecksum;
                }

                private static int ExerciseValueReferenceAndNullableValues()
                {
                    var nullable = new SortedSet<int?> { null, 2, 1 };
                    var values = new SortedDictionary<string, Payload?>
                    {
                        ["null"] = null,
                        ["one"] = new Payload(1),
                    };
                    var nullValue = values.ContainsValue(null);
                    var payloadValue = values["one"]?.Value ?? -1;
                    return (nullable.Min is null ? 1000 : 0) + nullable.Max!.Value * 100 +
                        (nullValue ? 10 : 0) + payloadValue;
                }

                public static int Trace() => 0;
            }
            """,
            [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10])
        {
            ExpectedReturnValues = ImmutableDictionary<int, int>.Empty.Add(6, 1),
            CaptureCompilerDiagnostics = true,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });
}
