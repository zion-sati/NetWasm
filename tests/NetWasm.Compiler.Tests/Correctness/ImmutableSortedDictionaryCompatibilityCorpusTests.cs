namespace NetWasm.Compiler.Tests.Correctness;

public sealed class ImmutableSortedDictionaryCompatibilityCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void ImmutableSortedDictionariesMatchDesktopAcrossProfilesAndTargets() =>
        runner.Run(new(
            "ImmutableSortedDictionaryCompatibilityCorpus",
            "NetWasm.Correctness.ImmutableSortedDictionaries",
            Source,
            [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11])
        {
            CaptureCompilerDiagnostics = true,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });

    private const string Source = """
        using System;
        using System.Collections;
        using System.Collections.Generic;
        using System.Collections.Immutable;

        namespace NetWasm.Correctness.ImmutableSortedDictionaries;

        public sealed class DescendingComparer : IComparer<int>
        {
            public int Compare(int left, int right) => right.CompareTo(left);
        }

        public static class EntryPoint
        {
            public static int Trace() => 0;

            public static int Run(int input) => input switch
            {
                0 => PersistentOperationsAndOrdering(),
                1 => ComparersAndCanonicalKeys(),
                2 => BuilderMutationAndInvalidation(),
                3 => InterfaceContracts(),
                4 => FactorySelectors(),
                5 => FailureContracts(),
                6 => EmptyAndNoOpSharing(),
                7 => RangeOperations(),
                8 => CopiedEnumeratorDisposal(),
                9 => EmptyInterfaceEnumeration(),
                10 => BuilderSyncRootAndMissingValue(),
                _ => UnknownCountConstruction(),
            };

            private static int PersistentOperationsAndOrdering()
            {
                var original = ImmutableSortedDictionary.CreateRange(new[]
                {
                    new KeyValuePair<int, int>(3, 30),
                    new KeyValuePair<int, int>(1, 10),
                    new KeyValuePair<int, int>(2, 20),
                });
                var changed = original.Add(4, 40).SetItem(2, 22).Remove(1);
                var checksum = 0;
                foreach (var pair in changed)
                {
                    checksum = checksum * 10 + pair.Key;
                }

                return original.Count * 10000 + changed.Count * 1000 +
                    original[1] + changed[2] + checksum;
            }

            private static int ComparersAndCanonicalKeys()
            {
                var values = ImmutableSortedDictionary.Create<int, string>(new DescendingComparer());
                values = values.Add(1, "one").Add(3, "three").Add(2, "two");
                var checksum = 0;
                foreach (var key in values.Keys)
                {
                    checksum = checksum * 10 + key;
                }

                return values.KeyComparer.Compare(3, 1) < 0 ? checksum : -1;
            }

            private static int BuilderMutationAndInvalidation()
            {
                var builder = ImmutableSortedDictionary.Create<int, int>().Add(2, 20).ToBuilder();
                var enumerator = builder.GetEnumerator();
                _ = enumerator.MoveNext();
                builder.Add(1, 10);
                var invalidated = false;
                try
                {
                    _ = enumerator.MoveNext();
                }
                catch (InvalidOperationException)
                {
                    invalidated = true;
                }

                var snapshot = builder.ToImmutable();
                builder[2] = 22;
                return (invalidated ? 1000 : 0) + snapshot.Count * 100 +
                    snapshot[1] + builder[2];
            }

            private static int InterfaceContracts()
            {
                IImmutableDictionary<int, int> immutable = ImmutableSortedDictionary.Create<int, int>().Add(2, 20).Add(1, 10);
                IReadOnlyDictionary<int, int> readOnly = immutable;
                IDictionary boxed = (IDictionary)immutable;
                var keys = 0;
                foreach (var key in readOnly.Keys)
                {
                    keys = keys * 10 + key;
                }

                return keys + (boxed.IsReadOnly ? 1000 : 0) +
                    (boxed.Contains(1) ? 100 : 0) + (immutable.Contains(new KeyValuePair<int, int>(2, 20)) ? 10 : 0);
            }

            private static int FactorySelectors()
            {
                var values = new[] { "a", "bb", "ccc" }.ToImmutableSortedDictionary(
                    value => value.Length,
                    value => value.Length * 10);
                return values.Count * 100 + values[1] + values[3];
            }

            private static int FailureContracts()
            {
                var values = ImmutableSortedDictionary.Create<int, int>().Add(1, 10);
                var failures = 0;
                try { values.Add(1, 11); } catch (ArgumentException) { failures++; }
                try { _ = values[9]; } catch (KeyNotFoundException) { failures++; }
                try { ImmutableSortedDictionary.CreateRange<int, int>(null!); } catch (ArgumentNullException) { failures++; }
                return failures;
            }

            private static int EmptyAndNoOpSharing()
            {
                var empty = ImmutableSortedDictionary<int, int>.Empty;
                var removed = empty.Remove(1);
                var cleared = ImmutableSortedDictionary.Create<int, int>().Add(1, 10).Clear();
                return (ReferenceEquals(empty, removed) ? 1000 : 0) +
                    (ReferenceEquals(empty, cleared) ? 100 : 0) +
                    (empty.Count == 0 ? 10 : 0) + (empty.GetEnumerator().MoveNext() ? 1 : 0);
            }

            private static int RangeOperations()
            {
                var values = ImmutableSortedDictionary.CreateRange(new[]
                {
                    new KeyValuePair<int, int>(1, 10),
                    new KeyValuePair<int, int>(2, 20),
                    new KeyValuePair<int, int>(3, 30),
                });
                var set = values.SetItems(new[]
                {
                    new KeyValuePair<int, int>(2, 200),
                    new KeyValuePair<int, int>(4, 40),
                });
                var removed = set.RemoveRange(new[] { 1, 9 });
                return values.Count * 1000 + set.Count * 100 + removed.Count * 10 + removed[2] / 100;
            }

            private static int CopiedEnumeratorDisposal()
            {
                var first = ImmutableSortedDictionary.Create<int, int>().Add(1, 10).Add(2, 20).GetEnumerator();
                var copy = first;
                first.Dispose();
                try
                {
                    copy.MoveNext();
                    return 0;
                }
                catch (ObjectDisposedException)
                {
                    return 1;
                }
            }

            private static int EmptyInterfaceEnumeration()
            {
                IEnumerable<KeyValuePair<int, int>> values = ImmutableSortedDictionary<int, int>.Empty;
                using var enumerator = values.GetEnumerator();
                var before = enumerator.MoveNext();
                enumerator.Reset();
                var after = enumerator.MoveNext();
                return !before && !after ? 1 : 0;
            }

            private static int BuilderSyncRootAndMissingValue()
            {
                var builder = ImmutableSortedDictionary.CreateBuilder<int, string>();
                builder.Add(1, "one");
                ICollection collection = builder;
                var syncRoot = collection.SyncRoot;
                var missing = builder.TryGetValue(2, out var value);
                return (ReferenceEquals(syncRoot, collection.SyncRoot) ? 100 : 0) +
                    (!missing ? 10 : 0) + (value is null ? 1 : 0);
            }

            private static int UnknownCountConstruction()
            {
                var values = ImmutableSortedDictionary.CreateRange(UnknownCount());
                var checksum = 0;
                foreach (var pair in values)
                {
                    checksum = checksum * 10 + pair.Key;
                }

                return values.Count * 1000 + checksum;
            }

            private static IEnumerable<KeyValuePair<int, int>> UnknownCount()
            {
                yield return new KeyValuePair<int, int>(3, 30);
                yield return new KeyValuePair<int, int>(1, 10);
                yield return new KeyValuePair<int, int>(2, 20);
            }
        }
        """;
}
