namespace NetWasm.Compiler.Tests.Correctness;

// This is an end-to-end differential acceptance fixture, not a unit test.
public sealed class FrozenSetCompatibilityCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void FrozenSetsMatchDesktopForEveryCompilerProfileAndTarget() =>
        runner.Run(new(
            "FrozenSetCompatibilityCorpus",
            "NetWasm.Correctness.FrozenSets",
            Source,
            [0, 1, 2, 3, 4, 5, 6])
        {
            CaptureCompilerDiagnostics = true,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });

    private const string Source = """
        using System;
        using System.Collections;
        using System.Collections.Frozen;
        using System.Collections.Generic;

        namespace NetWasm.Correctness.FrozenSets;

        public enum SmallCode : byte
        {
            Zero,
            One,
            Two,
            Three,
        }

        public static class EntryPoint
        {
            public static int Trace() => 0;

            public static int Run(int input) => input switch
            {
                0 => EmptyAndFactoryOperations(),
                1 => DuplicateAndComparerOperations(),
                2 => ValueTypeAndEnumOperations(),
                3 => AlternateLookupOperations(),
                4 => SetRelationOperations(),
                5 => InterfaceAndEnumeratorOperations(),
                _ => ImmutableFailureOperations(),
            };

            private static int EmptyAndFactoryOperations()
            {
                var empty = FrozenSet<int>.Empty;
                var set = FrozenSet.Create(2, 4, 6, 8);
                return empty.Count * 10000 +
                    (empty.SetEquals(Array.Empty<int>()) ? 1000 : 0) +
                    set.Count * 100 +
                    (set.Contains(6) ? 10 : 0) + (set.Contains(7) ? 1 : 0);
            }

            private static int DuplicateAndComparerOperations()
            {
                var set = FrozenSet.Create(
                    StringComparer.OrdinalIgnoreCase,
                    "red",
                    "RED",
                    "blue",
                    "green");
                return set.Count * 100 +
                    (set.Contains("Red") ? 10 : 0) +
                    (set.Contains("missing") ? 1 : 0);
            }

            private static int ValueTypeAndEnumOperations()
            {
                var numbers = FrozenSet.Create(9, 1, 5, 3, 7, 11, 13, 15, 17, 19, 21);
                var codes = FrozenSet.Create(SmallCode.Zero, SmallCode.Two, SmallCode.Three);
                return numbers.Count * 1000 + codes.Count * 100 +
                    (numbers.Contains(15) ? 10 : 0) +
                    (codes.Contains(SmallCode.One) ? 1 : 0);
            }

            private static int AlternateLookupOperations()
            {
                var set = FrozenSet.Create(
                    StringComparer.OrdinalIgnoreCase,
                    "alpha",
                    "beta",
                    "gamma",
                    "delta",
                    "epsilon");
                var lookup = set.GetAlternateLookup<ReadOnlySpan<char>>();
                ReadOnlySpan<char> found = "BETA";
                ReadOnlySpan<char> missing = "zeta";
                return set.Count * 100 +
                    (lookup.Contains(found) ? 10 : 0) +
                    (lookup.Contains(missing) ? 1 : 0);
            }

            private static int SetRelationOperations()
            {
                var set = FrozenSet.Create(1, 2, 3, 4);
                return (set.IsSubsetOf(new[] { 0, 1, 2, 3, 4, 5 }) ? 10000 : 0) +
                    (set.IsProperSubsetOf(new[] { 1, 2, 3, 4, 5 }) ? 1000 : 0) +
                    (set.IsSupersetOf(new[] { 2, 4 }) ? 100 : 0) +
                    (set.Overlaps(new[] { 8, 4 }) ? 10 : 0) +
                    (set.SetEquals(new[] { 4, 3, 2, 1, 1 }) ? 1 : 0);
            }

            private static int InterfaceAndEnumeratorOperations()
            {
                var set = FrozenSet.Create(3, 5, 7);
                var readOnly = (IReadOnlySet<int>)set;
                var collection = (ICollection<int>)set;
                var copied = new int[3];
                collection.CopyTo(copied, 0);

                var enumerator = set.GetEnumerator();
                var invalidCurrent = 0;
                try
                {
                    _ = enumerator.Current;
                }
                catch (InvalidOperationException)
                {
                    invalidCurrent = 1;
                }

                var sum = 0;
                foreach (var value in readOnly)
                {
                    sum += value;
                }

                return readOnly.Count * 1000 + sum * 100 +
                    (copied[0] + copied[1] + copied[2]) * 10 + invalidCurrent;
            }

            private static int ImmutableFailureOperations()
            {
                var set = FrozenSet.Create(1, 2, 3);
                var mutable = (ISet<int>)set;
                var failures = 0;
                try { mutable.Add(4); } catch (NotSupportedException) { failures++; }
                try { mutable.Remove(1); } catch (NotSupportedException) { failures++; }
                try { mutable.Clear(); } catch (NotSupportedException) { failures++; }
                try { mutable.UnionWith(new[] { 4 }); } catch (NotSupportedException) { failures++; }
                return set.Count * 100 + failures;
            }
        }
        """;
}
