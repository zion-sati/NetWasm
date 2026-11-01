namespace NetWasm.Compiler.Tests.Correctness;

// This is an end-to-end differential acceptance fixture, not a unit test.
public sealed class ImmutableHashSetCompatibilityCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void ImmutableHashSetsMatchDesktopForEveryCompilerProfileAndTarget() =>
        runner.Run(new(
            "ImmutableHashSetCompatibilityCorpus",
            "NetWasm.Correctness.ImmutableHashSets",
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

        namespace NetWasm.Correctness.ImmutableHashSets;

        public static class EntryPoint
        {
            public static int Trace() => 0;

            public static int Run(int input) => input switch
            {
                0 => PersistentAddsAndRemoval(),
                1 => SetAlgebra(),
                2 => RelationsAndQueries(),
                3 => BuilderSnapshots(),
                4 => ComparerAndReferenceLookup(),
                5 => InterfaceContracts(),
                6 => EmptyAndFailureContracts(),
                7 => StrongEnumerableSetAlgebra(),
                8 => CopiedEnumeratorDisposal(),
                9 => EmptyInterfaceEnumeration(),
                _ => ValueTypeCollisionBehavior(),
            };

            private static int PersistentAddsAndRemoval()
            {
                var original = ImmutableHashSet.CreateRange(new[] { 1, 2, 3 });
                var same = original.Add(2);
                var changed = original.Add(4).Remove(1);
                var sum = 0;
                foreach (var value in changed)
                {
                    sum += value;
                }

                return original.Count * 1000 + changed.Count * 100 +
                    sum * 10 + (ReferenceEquals(original, same) ? 1 : 0);
            }

            private static int SetAlgebra()
            {
                var values = ImmutableHashSet.CreateRange(new[] { 1, 2, 3 });
                var union = values.Union(new[] { 3, 4 });
                var intersection = values.Intersect(new[] { 2, 3, 8 });
                var except = values.Except(new[] { 1, 8 });
                var symmetric = values.SymmetricExcept(new[] { 3, 4 });

                return union.Count * 1000 + intersection.Count * 100 +
                    except.Count * 10 + symmetric.Count;
            }

            private static int RelationsAndQueries()
            {
                var values = ImmutableHashSet.CreateRange(new[] { 2, 4, 6 });
                return (values.Contains(4) ? 1000 : 0) +
                    (values.SetEquals(new[] { 6, 4, 2, 2 }) ? 100 : 0) +
                    (values.IsSubsetOf(new[] { 1, 2, 4, 6, 8 }) ? 10 : 0) +
                    (values.IsProperSubsetOf(new[] { 2, 4, 6, 8 }) ? 1 : 0) +
                    (values.IsSupersetOf(new[] { 2, 6 }) ? 10000 : 0) +
                    (values.Overlaps(new[] { 1, 6 }) ? 100000 : 0);
            }

            private static int BuilderSnapshots()
            {
                var original = ImmutableHashSet.Create(2, 4);
                var builder = original.ToBuilder();
                builder.Add(6);
                builder.Remove(2);
                var snapshot = builder.ToImmutable();
                builder.Add(8);

                return original.Count * 1000 + snapshot.Count * 100 +
                    builder.Count * 10 + (snapshot.Contains(6) && !snapshot.Contains(2) ? 1 : 0);
            }

            private static int ComparerAndReferenceLookup()
            {
                var values = ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "alpha");
                var same = values.Add("ALPHA");
                var found = values.TryGetValue("ALPHA", out var actual);

                return (ReferenceEquals(values, same) ? 1000 : 0) +
                    (found ? 100 : 0) + (actual == "alpha" ? 10 : 0) +
                    (same.KeyComparer.Equals("ALPHA", "alpha") ? 1 : 0);
            }

            private static int InterfaceContracts()
            {
                IImmutableSet<int> immutable = ImmutableHashSet.Create(3, 1, 2);
                IReadOnlySet<int> readOnly = immutable as IReadOnlySet<int> ??
                    throw new InvalidOperationException();
                ICollection<int> collection = (ICollection<int>)immutable;
                var copy = new int[5];
                collection.CopyTo(copy, 1);

                var rejected = 0;
                try { collection.Add(9); } catch (NotSupportedException) { rejected++; }
                try { collection.Clear(); } catch (NotSupportedException) { rejected++; }

                return (readOnly.Contains(2) ? 1000 : 0) +
                    (collection.IsReadOnly ? 100 : 0) + copy[1] + copy[2] + copy[3] +
                    rejected;
            }

            private static int EmptyAndFailureContracts()
            {
                var empty = ImmutableHashSet<int>.Empty;
                var same = empty.Remove(1);
                var failures = 0;
                try { _ = empty.Union(null!); } catch (ArgumentNullException) { failures++; }
                try { _ = ImmutableHashSet.CreateRange<int>(null!); } catch (ArgumentNullException) { failures++; }

                return (ReferenceEquals(empty, same) ? 1000 : 0) +
                    (empty.Count == 0 ? 100 : 0) +
                    (!empty.GetEnumerator().MoveNext() ? 10 : 0) + failures;
            }

            private static int StrongEnumerableSetAlgebra()
            {
                var values = ImmutableHashSet.Create(1, 2, 3, 4);
                var other = ImmutableHashSet.Create(3, 4, 5);
                return values.Union(other).Count * 10000 +
                    values.Intersect(other).Count * 1000 +
                    values.Except(other).Count * 100 +
                    (values.IsSupersetOf(ImmutableHashSet.Create(1, 3)) ? 10 : 0) +
                    (values.Overlaps(other) ? 1 : 0);
            }

            private static int CopiedEnumeratorDisposal()
            {
                var first = ImmutableHashSet.Create(1, 2, 3).GetEnumerator();
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
                IEnumerable<int> values = ImmutableHashSet<int>.Empty;
                using var enumerator = values.GetEnumerator();
                var before = enumerator.MoveNext();
                enumerator.Reset();
                var after = enumerator.MoveNext();
                return !before && !after ? 1 : 0;
            }

            private static int ValueTypeCollisionBehavior()
            {
                var values = ImmutableHashSet.Create(new Token(1), new Token(2), new Token(1));
                var changed = values.Add(new Token(3)).Remove(new Token(2));
                return values.Count * 100 + changed.Count * 10 +
                    (changed.Contains(new Token(3)) ? 1 : 0);
            }

            private readonly struct Token : IEquatable<Token>
            {
                private readonly int _value;

                public Token(int value) => _value = value;

                public bool Equals(Token other) => _value == other._value;

                public override bool Equals(object? obj) => obj is Token other && Equals(other);

                public override int GetHashCode() => 1;
            }
        }
        """;
}
