namespace NetWasm.Compiler.Tests.Correctness;

public sealed class ListCompatibilityCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void ListsMatchDesktopAcrossCompilerProfilesAndTargets() =>
        runner.Run(new(
            "ListCompatibilityCorpus",
            "NetWasm.Correctness.Lists",
            """
            using System;
            using System.Collections;
            using System.Collections.Generic;
            using System.Runtime.InteropServices;

            namespace NetWasm.Correctness.Lists;

            public static class EntryPoint
            {
                public static int Run(int input) => input switch
                {
                    0 => ConstructFromCollection(),
                    1 => GrowAndTrim(),
                    2 => AddAndInsertSelf(),
                    3 => SearchAndPredicates(),
                    4 => RemoveItems(),
                    5 => CopyAndSlice(),
                    6 => SortAndSearch(),
                    7 => ConvertAndVisit(),
                    8 => DetectEnumeratorMutation(),
                    9 => ExerciseNonGenericSurface(),
                    10 => MutateThroughCollectionsMarshal(),
                    _ => ValidateBounds(),
                };

                private static int ConstructFromCollection()
                {
                    ICollection<int> source = new[] { 2, 4, 6 };
                    var values = new List<int>(source);
                    return values.Count * 1000 + values.Capacity * 100 + Checksum(values);
                }

                private static int GrowAndTrim()
                {
                    var values = new List<int>();
                    values.Add(3);
                    int grown = values.EnsureCapacity(17);
                    values.AddRange(new[] { 5, 7 });
                    values.TrimExcess();
                    return grown * 100 + values.Capacity * 10 + values.Count;
                }

                private static int AddAndInsertSelf()
                {
                    var values = new List<int> { 1, 2, 3 };
                    values.AddRange(values);
                    values.InsertRange(2, values);
                    return Checksum(values);
                }

                private static int SearchAndPredicates()
                {
                    var values = new List<int> { 1, 4, 6, 4, 9 };
                    var evens = values.FindAll(static value => (value & 1) == 0);
                    return values.IndexOf(4) * 100000
                        + values.LastIndexOf(4) * 10000
                        + values.FindIndex(static value => value > 5) * 1000
                        + values.FindLastIndex(static value => value < 9) * 100
                        + evens.Count * 10
                        + (values.Exists(static value => value == 9)
                            && !values.TrueForAll(static value => value < 9) ? 1 : 0);
                }

                private static int RemoveItems()
                {
                    var values = new List<int> { 1, 2, 3, 2, 4, 2, 5 };
                    bool removed = values.Remove(2);
                    values.RemoveAt(1);
                    values.RemoveRange(1, 2);
                    int removedAll = values.RemoveAll(static value => (value & 1) == 0);
                    return (removed ? 10000 : 0) + removedAll * 1000 + Checksum(values);
                }

                private static int CopyAndSlice()
                {
                    var values = new List<int> { 1, 2, 3, 4, 5 };
                    var copy = new int[8];
                    values.CopyTo(1, copy, 2, 3);
                    var range = values.GetRange(1, 3);
                    var slice = values.Slice(2, 2);
                    return Checksum(copy) + Checksum(range) * 10 + Checksum(slice) * 100;
                }

                private static int SortAndSearch()
                {
                    var values = new List<int> { 7, 1, 5, 3 };
                    values.Sort();
                    int found = values.BinarySearch(5);
                    values.Sort(static (left, right) => right.CompareTo(left));
                    values.Reverse(1, 2);
                    return found * 100000 + Checksum(values);
                }

                private static int ConvertAndVisit()
                {
                    var values = new List<int> { 1, 2, 3 };
                    var converted = values.ConvertAll(static value => value * 10);
                    int total = 0;
                    converted.ForEach(value => total += value);
                    return total * 100 + Checksum(converted.AsReadOnly());
                }

                private static int DetectEnumeratorMutation()
                {
                    var values = new List<int> { 1, 2 };
                    var enumerator = values.GetEnumerator();
                    enumerator.MoveNext();
                    values[0] = 3;
                    try
                    {
                        enumerator.MoveNext();
                        return -1;
                    }
                    catch (InvalidOperationException)
                    {
                        return enumerator.Current;
                    }
                }

                private static int ExerciseNonGenericSurface()
                {
                    IList values = new List<int> { 1, 2 };
                    int index = values.Add(3);
                    values.Insert(1, 4);
                    values.Remove(2);
                    bool rejected = false;
                    try
                    {
                        values.Add("bad");
                    }
                    catch (ArgumentException)
                    {
                        rejected = true;
                    }
                    return index * 10000 + (rejected ? 1000 : 0) + (int)values[1]! * 100 + values.Count;
                }

                private static int MutateThroughCollectionsMarshal()
                {
                    var values = new List<int> { 2, 4, 6 };
                    CollectionsMarshal.SetCount(values, 5);
                    var span = CollectionsMarshal.AsSpan(values);
                    span[3] = 8;
                    span[4] = 10;
                    CollectionsMarshal.SetCount(values, 2);
                    return values.Capacity * 1000 + Checksum(values);
                }

                private static int ValidateBounds()
                {
                    var values = new List<int> { 1, 2, 3 };
                    int score = 0;
                    try { _ = values[-1]; }
                    catch (ArgumentOutOfRangeException exception) { score += exception.ParamName == "index" ? 1 : 0; }
                    try { values.Insert(4, 0); }
                    catch (ArgumentOutOfRangeException exception) { score += exception.ParamName == "index" ? 10 : 0; }
                    try { values.GetRange(2, 2); }
                    catch (ArgumentException) { score += 100; }
                    try { values.Capacity = 2; }
                    catch (ArgumentOutOfRangeException exception) { score += exception.ParamName == "value" ? 1000 : 0; }
                    return score;
                }

                private static int Checksum(IReadOnlyList<int> values)
                {
                    int result = 0;
                    for (int index = 0; index < values.Count; index++)
                    {
                        result = result * 11 + values[index];
                    }
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
