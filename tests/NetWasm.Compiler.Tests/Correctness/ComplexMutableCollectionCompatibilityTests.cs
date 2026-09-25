using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

// This is an end-to-end differential acceptance fixture, not a unit test.
[Collection(CorrectnessTestGroup.Name)]
public sealed class ComplexMutableCollectionCompatibilityTests(
    CorrectnessTestRunner runner)
{
    [Fact]
    public void PrioritySortedAndOrderedCollectionsMatchDesktopForEveryProfile() =>
        runner.Run(new(
            "ComplexMutableCollectionCompatibility",
            "NetWasm.Correctness.ComplexCollections",
            Source,
            [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15])
        {
            ExpectedReturnValues = ImmutableDictionary<int, int>.Empty
                .Add(2, 30).Add(4, 10).Add(8, 358).Add(9, 2).Add(11, 20),
            CaptureCompilerDiagnostics = true,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });

    private const string Source = """
        using System;
        using System.Collections;
        using System.Collections.Generic;

        namespace NetWasm.Correctness.ComplexCollections;

        public sealed class ReverseComparer : IComparer<int>
        {
            public int Compare(int left, int right) => right.CompareTo(left);
        }

        public sealed class ThrowingComparer : IComparer<int>
        {
            public int Compare(int left, int right) =>
                left == 13 || right == 13
                    ? throw new InvalidOperationException()
                    : left.CompareTo(right);
        }

        public sealed class Payload
        {
            public Payload(int value) => Value = value;
            public int Value { get; }
        }

        public static class EntryPoint
        {
            public static int Trace() => 0;

            public static int Run(int input) => input switch
            {
                0 => PriorityQueueOrdering(),
                1 => PriorityQueueCapacityAndRanges(),
                2 => PriorityQueueMutationInvalidatesEnumeration(),
                3 => SortedDictionaryOrderingAndInterfaces(),
                4 => SortedDictionaryMutationInvalidatesEnumeration(),
                5 => SortedListIndexAndCapacityContracts(),
                6 => SortedListFailureContracts(),
                7 => SortedSetRotationsAndRanges(),
                8 => SortedSetAlgebra(),
                9 => SortedSetMutationInvalidatesEnumeration(),
                10 => OrderedDictionaryOrderAndIndexes(),
                11 => OrderedDictionaryMutationInvalidatesEnumeration(),
                12 => NullableAndReferencePayloads(),
                13 => ComparerExceptionPropagation(),
                14 => PreserveCollectionRoots(),
                _ => OrderedDictionaryViewEnumeratorReset(),
            };

            private static int PriorityQueueOrdering()
            {
                var queue = new PriorityQueue<int, int>(new ReverseComparer());
                queue.Enqueue(10, 1);
                queue.Enqueue(20, 3);
                queue.Enqueue(30, 2);
                var first = queue.Dequeue();
                var second = queue.EnqueueDequeue(40, 4);
                return first * 100 + second + queue.Count;
            }

            private static int PriorityQueueCapacityAndRanges()
            {
                var queue = new PriorityQueue<int, int>(1);
                queue.EnqueueRange(new[] { (7, 2), (3, 1), (9, 3) });
                queue.EnqueueRange(new[] { 4, 5 }, 0);
                var capacity = queue.EnsureCapacity(17);
                var unorderedSum = 0;
                foreach (var item in queue.UnorderedItems)
                {
                    unorderedSum += item.Element + item.Priority;
                }
                queue.TrimExcess();
                return queue.Count * 10000 + (capacity >= 17 ? 1000 : 0) + unorderedSum;
            }

            private static int PriorityQueueMutationInvalidatesEnumeration()
            {
                var queue = new PriorityQueue<int, int>();
                queue.Enqueue(1, 1);
                queue.Enqueue(2, 2);
                var enumerator = queue.UnorderedItems.GetEnumerator();
                enumerator.MoveNext();
                queue.Enqueue(3, 0);
                try
                {
                    enumerator.MoveNext();
                    return -1;
                }
                catch (InvalidOperationException)
                {
                    return queue.TryPeek(out var element, out var priority)
                        ? element * 10 + priority
                        : -2;
                }
            }

            private static int SortedDictionaryOrderingAndInterfaces()
            {
                IDictionary<int, string> values =
                    new SortedDictionary<int, string>(new ReverseComparer())
                    {
                        [2] = "two",
                        [1] = "one",
                        [3] = "three",
                    };
                var result = 0;
                foreach (var pair in values)
                {
                    result = result * 10 + pair.Key;
                }
                return result * 10 + (values.ContainsKey(2) ? values[2].Length : 0);
            }

            private static int SortedDictionaryMutationInvalidatesEnumeration()
            {
                var values = new SortedDictionary<int, int> { [2] = 20, [1] = 10 };
                var enumerator = ((IEnumerable<KeyValuePair<int, int>>)values).GetEnumerator();
                enumerator.MoveNext();
                values.Remove(2);
                try
                {
                    enumerator.MoveNext();
                    return -1;
                }
                catch (InvalidOperationException)
                {
                    return values.TryGetValue(1, out var value) ? value : -2;
                }
            }

            private static int SortedListIndexAndCapacityContracts()
            {
                var values = new SortedList<int, int>(1)
                {
                    [3] = 30,
                    [1] = 10,
                    [2] = 20,
                };
                values.SetValueAtIndex(1, 25);
                var key = values.GetKeyAtIndex(1);
                var value = values.GetValueAtIndex(1);
                values.TrimExcess();
                return key * 1000 + value * 10 + values.Capacity;
            }

            private static int SortedListFailureContracts()
            {
                var values = new SortedList<int, int> { [1] = 2 };
                try
                {
                    values.Add(1, 3);
                    return -1;
                }
                catch (ArgumentException)
                {
                    return values.Remove(9) ? -2 : values.IndexOfKey(1);
                }
            }

            private static int SortedSetRotationsAndRanges()
            {
                var values = new SortedSet<int>();
                for (var index = 31; index >= 0; index--)
                {
                    values.Add((index * 17) % 37);
                }
                for (var index = 0; index < 12; index += 2)
                {
                    values.Remove(index);
                }
                var view = values.GetViewBetween(10, 20);
                var checksum = 0;
                foreach (var value in view)
                {
                    checksum = checksum * 31 + value;
                }
                return checksum ^ values.Min ^ values.Max;
            }

            private static int SortedSetAlgebra()
            {
                var values = new SortedSet<int> { 1, 2, 3, 4 };
                values.IntersectWith(new[] { 2, 3, 5 });
                values.UnionWith(new[] { 5, 7 });
                values.SymmetricExceptWith(new[] { 3, 8 });
                values.ExceptWith(new[] { 2 });
                return values.SetEquals(new[] { 5, 7, 8 }) &&
                    values.IsSupersetOf(new[] { 5, 8 }) && values.Overlaps(new[] { 1, 7 })
                    ? values.Count * 100 + values.Min * 10 + values.Max
                    : -1;
            }

            private static int SortedSetMutationInvalidatesEnumeration()
            {
                var values = new SortedSet<int> { 1, 2, 3 };
                var enumerator = ((IEnumerable<int>)values).GetEnumerator();
                enumerator.MoveNext();
                values.Add(4);
                try
                {
                    enumerator.MoveNext();
                    return -1;
                }
                catch (InvalidOperationException)
                {
                    return values.RemoveWhere(static value => (value & 1) == 0);
                }
            }

            private static int OrderedDictionaryOrderAndIndexes()
            {
                var values = new OrderedDictionary<string, int>();
                values.Add("a", 1);
                values.Add("c", 3);
                values.Insert(1, "b", 2);
                values.SetAt(2, "d", 4);
                values.SetAt(0, 10);
                var middle = values.GetAt(1);
                return values.IndexOf("d") * 1000 + middle.Value * 100 +
                    values["a"] * 10 + values.Count;
            }

            private static int OrderedDictionaryMutationInvalidatesEnumeration()
            {
                var values = new OrderedDictionary<int, int> { [1] = 10, [2] = 20 };
                IEnumerator enumerator = ((IEnumerable)values).GetEnumerator();
                enumerator.MoveNext();
                values.RemoveAt(0);
                try
                {
                    enumerator.MoveNext();
                    return -1;
                }
                catch (InvalidOperationException)
                {
                    values.EnsureCapacity(8);
                    values.TrimExcess();
                    return values.TryGetValue(2, out var value, out var index)
                        ? value + index
                        : -2;
                }
            }

            private static int NullableAndReferencePayloads()
            {
                var sorted = new SortedDictionary<int, Payload?>
                {
                    [2] = null,
                    [1] = new Payload(7),
                };
                var ordered = new OrderedDictionary<int, int?>
                {
                    [1] = null,
                    [2] = 9,
                };
                var priority = new PriorityQueue<Payload?, int>();
                priority.Enqueue(null, 0);
                priority.Enqueue(new Payload(4), 1);
                return sorted[1]!.Value * 100 + (sorted[2] is null ? 10 : 0) +
                    (ordered[1].HasValue ? 0 : 1) + (priority.Dequeue() is null ? 2 : 0);
            }

            private static int ComparerExceptionPropagation()
            {
                var values = new SortedSet<int>(new ThrowingComparer()) { 1, 2 };
                try
                {
                    values.Add(13);
                    return -1;
                }
                catch (InvalidOperationException)
                {
                    var queue = new PriorityQueue<int, int>(new ThrowingComparer());
                    queue.Enqueue(1, 1);
                    try
                    {
                        queue.Enqueue(13, 13);
                        return -2;
                    }
                    catch (InvalidOperationException)
                    {
                        return values.Count * 10 + queue.Count;
                    }
                }
            }

            private static int PreserveCollectionRoots()
            {
                var sorted = new SortedDictionary<int, Payload>();
                var ordered = new OrderedDictionary<int, Payload>();
                var priority = new PriorityQueue<Payload, int>();
                for (var index = 0; index < 48; index++)
                {
                    var payload = new Payload(index + 1);
                    sorted.Add(index, payload);
                    ordered.Add(index, payload);
                    priority.Enqueue(payload, 47 - index);
                }
                for (var index = 0; index < 128; index++)
                {
                    _ = new Payload(index);
                }
                return sorted[47].Value * 10000 + ordered.GetAt(0).Value.Value * 100 +
                    priority.Dequeue().Value;
            }

            private static int OrderedDictionaryViewEnumeratorReset()
            {
                var values = new OrderedDictionary<int, int>
                {
                    [1] = 10,
                    [2] = 20,
                };
                IEnumerator keys = ((IEnumerable)values.Keys).GetEnumerator();
                IEnumerator entries = ((IEnumerable)values.Values).GetEnumerator();
                keys.MoveNext();
                entries.MoveNext();
                keys.Reset();
                entries.Reset();
                keys.MoveNext();
                entries.MoveNext();
                return (int)keys.Current! * 100 + (int)entries.Current!;
            }
        }
        """;
}
