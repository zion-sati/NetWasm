namespace NetWasm.Compiler.Tests.Correctness;

// This is an end-to-end differential acceptance fixture, not a unit test.
public sealed class ImmutableArrayCompatibilityCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void ImmutableArraysMatchDesktopForEveryCompilerProfileAndTarget() =>
        runner.Run(new(
            "ImmutableArrayCompatibilityCorpus",
            "NetWasm.Correctness.ImmutableArrays",
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
        using System.Collections.Generic;
        using System.Collections.Immutable;
        using System.Runtime.InteropServices;

        namespace NetWasm.Correctness.ImmutableArrays;

        public static class EntryPoint
        {
            public static int Trace() => 0;

            public static int Run(int input) => input switch
            {
                0 => PersistentOperations(),
                1 => BuilderSnapshots(),
                2 => InterfacesAndFailures(),
                3 => MarshalAliasing(),
                4 => EnumerableFactories(),
                5 => SpanAndTypeFiltering(),
                _ => EqualityAndDefaults(),
            };

            private static int PersistentOperations()
            {
                var original = ImmutableArray.Create(1, 2, 3);
                var changed = original.Add(4).Insert(1, 9).Remove(2).SetItem(0, 8);
                return original.Length * 10000 + original[0] * 1000 +
                    changed.Length * 100 + changed[0] * 10 + changed[1];
            }

            private static int BuilderSnapshots()
            {
                var builder = ImmutableArray.CreateBuilder<int>(4);
                builder.AddRange(2, 4, 6);
                var first = builder.ToImmutable();
                builder[0] = 1;
                builder.Add(8);
                var moved = builder.MoveToImmutable();
                return first.Length * 10000 + first[0] * 1000 +
                    moved.Length * 100 + moved[0] * 10 + moved[3];
            }

            private static int InterfacesAndFailures()
            {
                IImmutableList<int> immutable = ImmutableArray.Create(3, 1, 2);
                IReadOnlyList<int> readOnly = immutable;
                IList boxed = (IList)immutable;
                var result = readOnly[0] * 100 + boxed.IndexOf(1) * 10;
                try
                {
                    _ = default(ImmutableArray<int>).Length;
                    return -1;
                }
                catch (NullReferenceException)
                {
                    return result + (boxed.IsReadOnly ? 1 : 0);
                }
            }

            private static int MarshalAliasing()
            {
                var storage = new[] { 2, 4, 6 };
                var immutable = ImmutableCollectionsMarshal.AsImmutableArray(storage);
                var roundTrip = ImmutableCollectionsMarshal.AsArray(immutable)!;
                storage[1] = 9;
                var builder = ImmutableArray.CreateBuilder<int>(3);
                builder.AddRange(1, 3, 5);
                var memory = ImmutableCollectionsMarshal.AsMemory(builder);
                memory.Span[1] = 8;
                return (ReferenceEquals(storage, roundTrip) ? 1000 : 0) +
                    immutable[0] * 100 + immutable[1] * 10 + immutable[2] +
                    builder[1] +
                    (ImmutableCollectionsMarshal.AsArray(default(ImmutableArray<int>)) is null
                        ? 1
                        : 0) +
                    (ImmutableCollectionsMarshal.AsMemory<int>(null).IsEmpty ? 1 : 0);
            }

            private static int EnumerableFactories()
            {
                var values = ImmutableArray.CreateRange(UnknownCount());
                ReadOnlySpan<char> argument = "abc";
                var mapped = ImmutableArray.CreateRange<int, ReadOnlySpan<char>, int>(
                    values,
                    static (value, text) => value + text.Length,
                    argument);
                var sliced = ImmutableArray.CreateRange<int, ReadOnlySpan<char>, int>(
                    values,
                    1,
                    2,
                    static (value, text) => value * text.Length,
                    argument);
                return values.Length * 10000 + mapped[0] * 1000 +
                    mapped[2] * 100 + sliced[0] * 10 + sliced[1];
            }

            private static IEnumerable<int> UnknownCount()
            {
                yield return 2;
                yield return 4;
                yield return 6;
            }

            private static int SpanAndTypeFiltering()
            {
                var values = ImmutableArray.Create(2, 4, 6, 8);
                var span = values.AsSpan(1, 2);
                var objects = ImmutableArray.Create<object>("ignored", 3, 5L, 7);
                var sum = 0;
                foreach (var value in objects.OfType<int>())
                {
                    sum += value;
                }

                return span.Length * 1000 + span[0] * 100 + span[1] * 10 + sum;
            }

            private static int EqualityAndDefaults()
            {
                ImmutableArray<int> first = default;
                ImmutableArray<int> second = default;
                var empty = ImmutableArray<int>.Empty;
                return (first == second ? 1000 : 0) +
                    (first.Equals(second) ? 100 : 0) +
                    (empty != first ? 10 : 0) +
                    (empty.IsEmpty ? 1 : 0);
            }
        }
        """;
}
