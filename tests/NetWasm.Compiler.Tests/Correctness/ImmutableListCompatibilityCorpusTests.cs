using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

// This is an end-to-end differential acceptance fixture, not a unit test.
[Collection(CorrectnessTestGroup.Name)]
public sealed class ImmutableListCompatibilityCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void ImmutableListsMatchDesktopForEveryCompilerProfileAndTarget() =>
        runner.Run(new(
            "ImmutableListCompatibilityCorpus",
            "NetWasm.Correctness.ImmutableLists",
            Source,
            [0, 1, 2, 3, 4, 5, 6, 7, 8])
        {
            ExpectedReturnValues = ImmutableDictionary<int, int>.Empty.Add(4, 41),
            CaptureCompilerDiagnostics = true,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });

    private const string Source = """
        using System;
        using System.Collections;
        using System.Collections.Generic;
        using System.Collections.Immutable;

        namespace NetWasm.Correctness.ImmutableLists;

        public static class EntryPoint
        {
            public static int Trace() => 0;

            public static int Run(int input) => input switch
            {
                0 => PersistentAddsAndIndexes(),
                1 => PersistentInsertsAndRemovals(),
                2 => RangeOperationsAndSearch(),
                3 => BuilderMutationAndSnapshot(),
                4 => BuilderEnumeratorInvalidation(),
                5 => PredicatesAndConversion(),
                6 => InterfaceContracts(),
                7 => FailureContracts(),
                _ => EmptyAndNoOpSharing(),
            };

            private static int PersistentAddsAndIndexes()
            {
                var original = ImmutableList.CreateRange(new[] { 1, 2, 3 });
                var extended = original.Add(4).AddRange(new[] { 5, 6 });
                return original.Count * 1000 + extended.Count * 100 +
                    original[2] * 10 + extended[5];
            }

            private static int PersistentInsertsAndRemovals()
            {
                var original = ImmutableList.Create(1, 2, 3, 4);
                var changed = original.Insert(1, 9).RemoveAt(3).SetItem(0, 8);
                return original[0] * 10000 + original.Count * 1000 +
                    changed[0] * 100 + changed[1] * 10 + changed.Count;
            }

            private static int RangeOperationsAndSearch()
            {
                var values = ImmutableList.CreateRange(new[] { 7, 1, 4, 1, 9, 2 });
                var inserted = values.InsertRange(2, new[] { 8, 6 });
                var sorted = inserted.Sort();
                var reversed = sorted.Reverse(1, 4);
                return sorted.BinarySearch(6) * 10000 +
                    sorted.IndexOf(1) * 1000 + sorted.LastIndexOf(1) * 100 +
                    reversed[1] * 10 + reversed.Count;
            }

            private static int BuilderMutationAndSnapshot()
            {
                var original = ImmutableList.Create(2, 4, 6);
                var builder = original.ToBuilder();
                builder.AddRange(new[] { 8, 10 });
                builder.InsertRange(1, new[] { 3, 5 });
                builder.Remove(6);
                builder[0] = 1;
                var snapshot = builder.ToImmutable();
                return original.Count * 10000 + snapshot.Count * 1000 +
                    snapshot[0] * 100 + snapshot[1] * 10 + snapshot[snapshot.Count - 1];
            }

            private static int BuilderEnumeratorInvalidation()
            {
                var builder = ImmutableList.Create(1, 2, 3).ToBuilder();
                var enumerator = builder.GetEnumerator();
                enumerator.MoveNext();
                builder.Add(4);
                try
                {
                    enumerator.MoveNext();
                    return -1;
                }
                catch (InvalidOperationException)
                {
                    return builder.Count * 10 + builder[0];
                }
            }

            private static int PredicatesAndConversion()
            {
                var values = ImmutableList.Create(1, 2, 3, 4, 5);
                var evens = values.FindAll(static value => (value & 1) == 0);
                var converted = values.ConvertAll(static value => value * value);
                return values.Exists(static value => value == 3) ?
                    evens.Count * 1000 + converted.FindIndex(static value => value == 16) * 100 +
                    values.FindLast(static value => value < 5) : -1;
            }

            private static int InterfaceContracts()
            {
                IImmutableList<int> immutable = ImmutableList.Create(3, 1, 2);
                IReadOnlyList<int> readOnly = immutable;
                IList boxed = (IList)immutable;
                var sum = 0;
                foreach (var value in readOnly)
                {
                    sum = sum * 10 + value;
                }

                return sum + (boxed.IsReadOnly ? 1000 : 0) +
                    (boxed.Contains(1) ? 100 : 0);
            }

            private static int FailureContracts()
            {
                var values = ImmutableList.Create(1, 2, 3);
                var failures = 0;
                try { _ = values[-1]; } catch (ArgumentOutOfRangeException) { failures++; }
                try { _ = values.Remove(9).Replace(9, 1); } catch (ArgumentException) { failures++; }
                try { values.AddRange(null!); } catch (ArgumentNullException) { failures++; }
                return failures;
            }

            private static int EmptyAndNoOpSharing()
            {
                var empty = ImmutableList<int>.Empty;
                var same = empty.Remove(1);
                var cleared = ImmutableList.Create(1, 2).Clear();
                return (ReferenceEquals(empty, same) ? 1000 : 0) +
                    (ReferenceEquals(empty, cleared) ? 100 : 0) +
                    (empty.Count == 0 ? 10 : 0) +
                    (empty.GetEnumerator().MoveNext() ? 1 : 0);
            }
        }
        """;
}
