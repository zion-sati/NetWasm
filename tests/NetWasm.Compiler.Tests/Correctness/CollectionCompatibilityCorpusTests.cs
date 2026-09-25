using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

[Collection(CorrectnessTestGroup.Name)]
public sealed class CollectionCompatibilityCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void CollectionsMatchDesktopAcrossCompilerProfilesAndTargets() =>
        runner.Run(new(
            "CollectionCompatibilityCorpus",
            "NetWasm.Correctness.Collections",
            """
            using System;
            using System.Collections;
            using System.Collections.Generic;
            using System.Runtime.InteropServices;

            namespace NetWasm.Correctness.Collections;

            public sealed class CollisionComparer : IEqualityComparer<int>
            {
                public bool Equals(int left, int right) => left == right;
                public int GetHashCode(int value) => 1;
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
                    0 => MutateDuringEnumeration(),
                    1 => GrowCollidingDictionary(),
                    2 => AddDuplicateKey(),
                    3 => ReadMissingKey(),
                    4 => GrowCollidingSet(),
                    5 => WrapAndGrowQueue(),
                    6 => ExerciseCovariantEnumeration(),
                    7 => MutateListThroughSpan(),
                    8 => MutateBitsThroughBytes(),
                    9 => MutateDictionaryThroughReferences(),
                    10 => PreserveCollectionRoots(),
                    11 => ExerciseNonGenericCollections(),
                    _ => ExerciseBoxedCollectionValues(),
                };

                private static int MutateDuringEnumeration()
                {
                    var values = new List<int> { 1, 2, 3 };
                    foreach (var value in values)
                    {
                        values.Add(value);
                    }
                    return -1;
                }

                private static int GrowCollidingDictionary()
                {
                    var values = new Dictionary<int, int>(1, new CollisionComparer());
                    for (var index = 0; index < 40; index++)
                    {
                        values.Add(index, index * 3);
                    }
                    values.Remove(7);
                    values[7] = 70;
                    return values.Count * 1000 + values[0] + values[7] + values[39];
                }

                private static int AddDuplicateKey()
                {
                    var values = new Dictionary<int, int> { [1] = 2 };
                    values.Add(1, 3);
                    return -1;
                }

                private static int ReadMissingKey() => new Dictionary<int, int>()[4];

                private static int GrowCollidingSet()
                {
                    var values = new HashSet<int>(new CollisionComparer());
                    for (var index = 0; index < 40; index++)
                    {
                        values.Add(index % 23);
                    }
                    values.Remove(4);
                    return values.Count * 100 + (values.Contains(22) ? 10 : 0) +
                        (values.Contains(4) ? 1 : 0);
                }

                private static int WrapAndGrowQueue()
                {
                    var values = new Queue<int>(2);
                    values.Enqueue(1);
                    values.Enqueue(2);
                    values.Dequeue();
                    values.Enqueue(3);
                    values.Enqueue(4);
                    return values.Dequeue() * 100 + values.Dequeue() * 10 + values.Dequeue();
                }

                private static int ExerciseCovariantEnumeration()
                {
                    IEnumerable<string> strings = new List<string> { "a", "bb" };
                    IEnumerable<object> objects = strings;
                    var total = 0;
                    foreach (var value in objects)
                    {
                        total += value is null ? 100 : 1;
                    }
                    return total;
                }

                private static int ExerciseBoxedCollectionValues()
                {
                    IList boxed = new ArrayList { 3, "four" };
                    return boxed.Count * 10 + (int)boxed[0]!;
                }

                private static int MutateListThroughSpan()
                {
                    var values = new List<int> { 2, 4, 6 };
                    var span = CollectionsMarshal.AsSpan(values);
                    span[1] = 9;
                    CollectionsMarshal.SetCount(values, 5);
                    values[3] = 7;
                    return values[0] * 10000 + values[1] * 1000 + values[2] * 100 +
                        values[3] * 10 + values[4];
                }

                private static int MutateBitsThroughBytes()
                {
                    var values = new BitArray(16);
                    var bytes = CollectionsMarshal.AsBytes(values);
                    bytes[0] = 5;
                    bytes[1] = 2;
                    return (values[0] ? 1000 : 0) + (values[1] ? 100 : 0) +
                        (values[2] ? 10 : 0) + (values[9] ? 1 : 0);
                }

                private static int MutateDictionaryThroughReferences()
                {
                    var values = new Dictionary<int, int>();
                    ref var added = ref CollectionsMarshal.GetValueRefOrAddDefault(
                        values,
                        5,
                        out var existed);
                    added = 12;
                    ref var existing = ref CollectionsMarshal.GetValueRefOrAddDefault(
                        values,
                        5,
                        out var existsNow);
                    existing += 3;
                    return (existed ? 1000 : 0) + (existsNow ? 100 : 0) + values[5];
                }

                private static int PreserveCollectionRoots()
                {
                    var list = new List<Payload>();
                    var dictionary = new Dictionary<int, Payload>();
                    for (var index = 0; index < 48; index++)
                    {
                        var payload = new Payload(index + 1);
                        list.Add(payload);
                        dictionary.Add(index, payload);
                    }
                    for (var index = 0; index < 128; index++)
                    {
                        _ = new Payload(index);
                    }
                    return list[47].Value * 100 + dictionary[0].Value;
                }

                private static int ExerciseNonGenericCollections()
                {
                    var list = new ArrayList { 1, 2, 3 };
                    list.Insert(1, 5);
                    var bits = new BitArray(new[] { true, false, true });
                    var total = 0;
                    foreach (int value in list)
                    {
                        total = total * 10 + value;
                    }
                    foreach (bool value in bits)
                    {
                        total = total * 2 + (value ? 1 : 0);
                    }
                    return total;
                }

                public static int Trace() => 0;
            }
            """,
            [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12])
        {
            ExpectedExceptionTypes = ImmutableDictionary<int, string>.Empty
                .Add(0, "System.InvalidOperationException")
                .Add(2, "System.ArgumentException")
                .Add(3, "System.Collections.Generic.KeyNotFoundException"),
            CaptureCompilerDiagnostics = true,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });
}
