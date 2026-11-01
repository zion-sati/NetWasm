namespace NetWasm.Compiler.Tests.Correctness;

public sealed class ObjectModelCollectionCompatibilityCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void ObjectModelCollectionsMatchDesktopAcrossCompilerProfilesAndTargets() =>
        runner.Run(new(
            "ObjectModelCollectionCompatibilityCorpus",
            "NetWasm.Correctness.ObjectModelCollections",
            """
            using System;
            using System.Collections;
            using System.Collections.Generic;
            using System.Collections.ObjectModel;

            namespace NetWasm.Correctness.ObjectModelCollections;

            public sealed class TrackingCollection<T> : Collection<T>
            {
                public int Score { get; private set; }

                protected override void ClearItems()
                {
                    Score = Score * 10 + 1;
                    base.ClearItems();
                }

                protected override void InsertItem(int index, T item)
                {
                    Score = Score * 10 + 2;
                    base.InsertItem(index, item);
                }

                protected override void RemoveItem(int index)
                {
                    Score = Score * 10 + 3;
                    base.RemoveItem(index);
                }

                protected override void SetItem(int index, T item)
                {
                    Score = Score * 10 + 4;
                    base.SetItem(index, item);
                }
            }

            public static class EntryPoint
            {
                public static int Run(int input) => input switch
                {
                    0 => ExerciseCollection(),
                    1 => ExerciseVirtualHooks(),
                    2 => ExerciseNonGenericCollection(),
                    3 => ExerciseCollectionCopyTo(),
                    4 => ExerciseCollectionFailures(),
                    5 => ExerciseReadOnlyCollection(),
                    6 => ExerciseReadOnlyMutations(),
                    7 => ExerciseBackingCollectionContracts(),
                    8 => ExerciseCollectionBuilder(),
                    9 => ExerciseReadOnlyFailures(),
                    _ => ExerciseReadOnlyBackingGuards(),
                };

                private static int ExerciseCollection()
                {
                    var values = new Collection<int> { 2, 4 };
                    values.Insert(1, 3);
                    values[0] = 1;
                    bool removed = values.Remove(3);
                    values.Add(5);
                    return removed && values.Count == 3 && values.Contains(4) && values.IndexOf(5) == 2
                        ? Checksum(values)
                        : -1;
                }

                private static int ExerciseVirtualHooks()
                {
                    var values = new TrackingCollection<int>();
                    values.Add(1);
                    values.Insert(0, 2);
                    values[1] = 3;
                    values.RemoveAt(0);
                    values.Clear();
                    return values.Score * 10 + values.Count;
                }

                private static int ExerciseNonGenericCollection()
                {
                    IList values = new Collection<string>();
                    int first = values.Add("one");
                    values.Insert(0, "zero");
                    values[1] = "ONE";
                    bool contains = values.Contains("zero") && !values.Contains(42);
                    int index = values.IndexOf("ONE");
                    values.Remove("zero");
                    return first == 0 && contains && index == 1 && (string)values[0]! == "ONE"
                        ? values.Count
                        : -1;
                }

                private static int ExerciseCollectionCopyTo()
                {
                    ICollection values = new Collection<string> { "a", "b" };
                    var objects = new object?[4];
                    values.CopyTo(objects, 1);
                    return objects[0] is null && (string)objects[1]! == "a" && (string)objects[2]! == "b"
                        ? objects.Length * 100 + values.Count
                        : -1;
                }

                private static int ExerciseCollectionFailures()
                {
                    int score = 0;
                    var values = new Collection<int> { 1 };
                    IList list = values;
                    try { list.Add(null); }
                    catch (ArgumentNullException exception) { score += exception.ParamName == "value" ? 1 : 0; }
                    try { list.Add("bad"); }
                    catch (ArgumentException exception) { score += exception.ParamName == "value" ? 10 : 0; }
                    try { values.Insert(2, 2); }
                    catch (ArgumentOutOfRangeException exception) { score += exception.ParamName == "index" ? 100 : 0; }
                    try { values.RemoveAt(1); }
                    catch (ArgumentOutOfRangeException exception) { score += exception.ParamName == "index" ? 1000 : 0; }
                    try { ((ICollection)values).CopyTo(new int[1], -1); }
                    catch (ArgumentOutOfRangeException exception) { score += exception.ParamName == "index" ? 10000 : 0; }
                    return score;
                }

                private static int ExerciseReadOnlyCollection()
                {
                    var backing = new List<int> { 2, 4, 6 };
                    var values = new ReadOnlyCollection<int>(backing);
                    backing[1] = 5;
                    var copy = new int[5];
                    values.CopyTo(copy, 1);
                    int enumerated = 0;
                    foreach (int value in values) enumerated = enumerated * 10 + value;
                    bool empty = !ReadOnlyCollection<int>.Empty.GetEnumerator().MoveNext();
                    return empty && values.Contains(5) && values.IndexOf(6) == 2
                        ? enumerated * 1000 + Checksum(copy)
                        : -1;
                }

                private static int ExerciseReadOnlyMutations()
                {
                    int score = 0;
                    var values = new ReadOnlyCollection<int>(new List<int> { 1 });
                    ICollection<int> generic = values;
                    IList nongeneric = values;
                    try { generic.Add(2); }
                    catch (NotSupportedException) { score += 1; }
                    try { generic.Remove(1); }
                    catch (NotSupportedException) { score += 10; }
                    try { ((IList<int>)values)[0] = 2; }
                    catch (NotSupportedException) { score += 100; }
                    try { nongeneric.Clear(); }
                    catch (NotSupportedException) { score += 1000; }
                    try { nongeneric[0] = 2; }
                    catch (NotSupportedException) { score += 10000; }
                    return score;
                }

                private static int ExerciseBackingCollectionContracts()
                {
                    int[] backing = [1, 2];
                    var mutableWrapper = new Collection<int>(backing);
                    var readOnlyWrapper = new ReadOnlyCollection<int>(backing);
                    ICollection mutable = mutableWrapper;
                    ICollection readOnly = readOnlyWrapper;
                    return ((IList)mutableWrapper).IsFixedSize
                        && ((IList)readOnlyWrapper).IsFixedSize
                        && ReferenceEquals(mutable.SyncRoot, ((ICollection)backing).SyncRoot)
                        && ReferenceEquals(readOnly.SyncRoot, ((ICollection)backing).SyncRoot)
                        ? 1
                        : -1;
                }

                private static int ExerciseCollectionBuilder()
                {
                    ReadOnlyCollection<int> values = [3, 1, 4];
                    return Checksum(values);
                }

                private static int ExerciseReadOnlyFailures()
                {
                    int score = 0;
                    try { _ = new ReadOnlyCollection<int>(null!); }
                    catch (ArgumentNullException exception) { score += exception.ParamName == "list" ? 1 : 0; }

                    ICollection values = new ReadOnlyCollection<string>(new List<string> { "a" });
                    try { values.CopyTo(null!, 0); }
                    catch (ArgumentNullException exception) { score += exception.ParamName == "array" ? 10 : 0; }
                    try { values.CopyTo(new object[1], -1); }
                    catch (ArgumentOutOfRangeException exception) { score += exception.ParamName == "index" ? 100 : 0; }
                    try { values.CopyTo(new object[0], 0); }
                    catch (ArgumentException) { score += 1000; }
                    return score;
                }

                private static int ExerciseReadOnlyBackingGuards()
                {
                    int score = 0;
                    IList<int> backing = new ReadOnlyCollection<int>(new List<int> { 1 });
                    var values = new Collection<int>(backing);
                    try { values.Add(2); }
                    catch (NotSupportedException) { score += 1; }
                    try { values.Clear(); }
                    catch (NotSupportedException) { score += 10; }
                    try { values.Insert(0, 2); }
                    catch (NotSupportedException) { score += 100; }
                    try { values.Remove(1); }
                    catch (NotSupportedException) { score += 1000; }
                    try { values.RemoveAt(0); }
                    catch (NotSupportedException) { score += 10000; }
                    try { values[0] = 2; }
                    catch (NotSupportedException) { score += 100000; }
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
