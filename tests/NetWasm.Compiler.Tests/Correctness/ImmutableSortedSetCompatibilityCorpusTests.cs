namespace NetWasm.Compiler.Tests.Correctness;

// This is an end-to-end differential acceptance fixture, not a unit test.
public sealed class ImmutableSortedSetCompatibilityCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void ImmutableSortedSetsMatchDesktopAcrossProfilesAndTargets() =>
        runner.Run(new(
            "ImmutableSortedSetCompatibilityCorpus",
            "NetWasm.Correctness.ImmutableSortedSets",
            Source,
            [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10])
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

        namespace NetWasm.Correctness.ImmutableSortedSets;

        public sealed class DescendingComparer : IComparer<int>
        {
            public int Compare(int left, int right) => right.CompareTo(left);
        }

        public static class EntryPoint
        {
            public static int Trace() => 0;

            public static int Run(int input) => input switch
            {
                0 => PersistentOrderingAndSharing(),
                1 => ComparersAndCanonicalValues(),
                2 => SetAlgebraAndRelations(),
                3 => BuilderMutationAndInvalidation(),
                4 => InterfaceContracts(),
                5 => ReverseAndIndexOperations(),
                6 => EmptyAndFailureContracts(),
                7 => StrongEnumerableSetAlgebra(),
                8 => CopiedEnumeratorDisposal(),
                9 => EmptyInterfaceEnumeration(),
                _ => BuilderSyncRootAndUnknownCount(),
            };

            private static int PersistentOrderingAndSharing()
            {
                var original = ImmutableSortedSet.CreateRange(new[] { 3, 1, 2 });
                var same = original.Add(2);
                var changed = original.Add(4).Remove(1);
                var checksum = 0;
                foreach (var value in changed)
                {
                    checksum = checksum * 10 + value;
                }

                return original.Count * 10000 + changed.Count * 1000 +
                    checksum + (ReferenceEquals(original, same) ? 1 : 0);
            }

            private static int ComparersAndCanonicalValues()
            {
                var values = ImmutableSortedSet.Create(new DescendingComparer(), 1, 3, 2);
                var checksum = 0;
                foreach (var value in values)
                {
                    checksum = checksum * 10 + value;
                }

                var same = values.Add(2);
                return checksum + (values.KeyComparer.Compare(3, 1) < 0 ? 100 : 0) +
                    (ReferenceEquals(values, same) ? 1 : 0);
            }

            private static int SetAlgebraAndRelations()
            {
                var values = ImmutableSortedSet.CreateRange(new[] { 1, 2, 3 });
                var union = values.Union(new[] { 3, 4 });
                var intersection = values.Intersect(new[] { 2, 3, 8 });
                var except = values.Except(new[] { 1, 8 });
                var symmetric = values.SymmetricExcept(new[] { 3, 4 });

                return union.Count * 1000 + intersection.Count * 100 +
                    except.Count * 10 + symmetric.Count +
                    (values.IsSubsetOf(new[] { 0, 1, 2, 3, 4 }) ? 10000 : 0) +
                    (values.IsProperSubsetOf(new[] { 0, 1, 2, 3, 4 }) ? 100000 : 0) +
                    (values.Overlaps(new[] { 9, 3 }) ? 1000000 : 0);
            }

            private static int BuilderMutationAndInvalidation()
            {
                var builder = ImmutableSortedSet.Create(2).ToBuilder();
                var enumerator = builder.GetEnumerator();
                _ = enumerator.MoveNext();
                builder.Add(1);
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
                builder.Add(3);
                return (invalidated ? 1000 : 0) + snapshot.Count * 100 +
                    builder.Count * 10 + (snapshot.Contains(1) && !snapshot.Contains(3) ? 1 : 0);
            }

            private static int InterfaceContracts()
            {
                IImmutableSet<int> immutable = ImmutableSortedSet.Create(3, 1, 2);
                ISet<int> set = (ISet<int>)immutable;
                ICollection<int> collection = (ICollection<int>)immutable;
                var copy = new int[5];
                collection.CopyTo(copy, 1);

                var rejected = 0;
                try { collection.Add(9); } catch (NotSupportedException) { rejected++; }
                try { collection.Clear(); } catch (NotSupportedException) { rejected++; }

                return (set.Contains(2) ? 1000 : 0) +
                    (collection.IsReadOnly ? 100 : 0) + copy[1] + copy[2] + copy[3] + rejected;
            }

            private static int ReverseAndIndexOperations()
            {
                var values = ImmutableSortedSet.CreateRange(new[] { 1, 2, 3, 4 });
                var forward = values[0] * 10 + values[3];
                var reverse = 0;
                foreach (var value in values.Reverse())
                {
                    reverse = reverse * 10 + value;
                }

                return forward + reverse + values.IndexOf(3) * 100 +
                    (values.Min == 1 ? 1000 : 0) + (values.Max == 4 ? 10000 : 0);
            }

            private static int EmptyAndFailureContracts()
            {
                var empty = ImmutableSortedSet<int>.Empty;
                var same = empty.Remove(1);
                var failures = 0;
                try { _ = empty.Union(null!); } catch (ArgumentNullException) { failures++; }
                try { _ = ImmutableSortedSet.CreateRange<int>(null!); } catch (ArgumentNullException) { failures++; }

                return (ReferenceEquals(empty, same) ? 1000 : 0) +
                    (empty.Count == 0 ? 100 : 0) +
                    (!empty.GetEnumerator().MoveNext() ? 10 : 0) + failures;
            }

            private static int StrongEnumerableSetAlgebra()
            {
                var values = ImmutableSortedSet.Create(1, 2, 3, 4);
                var other = ImmutableSortedSet.Create(3, 4, 5);
                return values.Union(other).Count * 10000 +
                    values.Intersect(other).Count * 1000 +
                    values.Except(other).Count * 100 +
                    (values.IsSupersetOf(ImmutableSortedSet.Create(1, 3)) ? 10 : 0) +
                    (values.Overlaps(other) ? 1 : 0);
            }

            private static int CopiedEnumeratorDisposal()
            {
                var first = ImmutableSortedSet.Create(1, 2, 3).GetEnumerator();
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
                IEnumerable<int> values = ImmutableSortedSet<int>.Empty;
                using var enumerator = values.GetEnumerator();
                var before = enumerator.MoveNext();
                enumerator.Reset();
                var after = enumerator.MoveNext();
                return !before && !after ? 1 : 0;
            }

            private static int BuilderSyncRootAndUnknownCount()
            {
                var builder = ImmutableSortedSet.CreateBuilder<int>();
                ICollection collection = builder;
                var syncRoot = collection.SyncRoot;
                builder.UnionWith(UnknownCount());
                return (ReferenceEquals(syncRoot, collection.SyncRoot) ? 10000 : 0) +
                    builder.Count * 1000 + builder[0] * 100 + builder[1] * 10 + builder[2];
            }

            private static IEnumerable<int> UnknownCount()
            {
                yield return 3;
                yield return 1;
                yield return 2;
                yield return 2;
            }
        }
        """;
}

