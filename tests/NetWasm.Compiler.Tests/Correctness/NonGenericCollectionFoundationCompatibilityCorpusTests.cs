namespace NetWasm.Compiler.Tests.Correctness;

public sealed class NonGenericCollectionFoundationCompatibilityCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void NonGenericCollectionFoundationsMatchDesktopAcrossCompilerProfilesAndTargets() =>
        runner.Run(new(
            "NonGenericCollectionFoundationCompatibilityCorpus",
            "NetWasm.Correctness.NonGenericCollectionFoundations",
            """
            #pragma warning disable 618

            using System;
            using System.Collections;

            namespace NetWasm.Correctness.NonGenericCollectionFoundations;

            public sealed class ComparableKey(int value) : IComparable
            {
                public int Value { get; } = value;
                public int CompareTo(object? other) => Value.CompareTo(((ComparableKey)other!).Value);
                public override string ToString() => Value.ToString();
            }

            public sealed class PlainKey(int value)
            {
                public int Value { get; } = value;
            }

            public sealed class ConstantHashProvider : IHashCodeProvider
            {
                public int Calls { get; private set; }

                public int GetHashCode(object obj)
                {
                    Calls++;
                    return 7;
                }
            }

            public sealed class PlainKeyComparer : IComparer
            {
                public int Compare(object? left, object? right) =>
                    ((PlainKey)left!).Value.CompareTo(((PlainKey)right!).Value);
            }

            public static class EntryPoint
            {
                public static int Run(int input) => input switch
                {
                    0 => ConstructNullKey(),
                    1 => SetNullKey(),
                    2 => FormatEntry(),
                    3 => DeconstructEntry(),
                    4 => UseComparableFallback(),
                    5 => UseCustomComparer(),
                    6 => ValidateIncomparableFailure(),
                    7 => GrowHashtable(),
                    8 => RemoveAndReuseCollisions(),
                    _ => CloneLegacyComparerTable(),
                };

                private static int ConstructNullKey()
                {
                    var entry = new DictionaryEntry(null!, 42);
                    return entry.Key is null && (int)entry.Value! == 42 && entry.ToString() == "[, 42]" ? 1 : -1;
                }

                private static int SetNullKey()
                {
                    var entry = new DictionaryEntry("key", "value");
                    entry.Key = null!;
                    entry.Value = null;
                    return entry.Key is null && entry.Value is null && entry.ToString() == "[, ]" ? 1 : -1;
                }

                private static int FormatEntry()
                {
                    var entry = new DictionaryEntry("alpha", 17);
                    return entry.ToString() == "[alpha, 17]" ? 11 : -1;
                }

                private static int DeconstructEntry()
                {
                    var entry = new DictionaryEntry("beta", 23);
                    entry.Deconstruct(out object key, out object? value);
                    return (string)key == "beta" && (int)value! == 23 ? 23 : -1;
                }

                private static int UseComparableFallback()
                {
                    var hashes = new ConstantHashProvider();
                    var values = new Hashtable(hashes, null) { [new ComparableKey(5)] = 50 };
                    return (int)values[new ComparableKey(5)]! + hashes.Calls;
                }

                private static int UseCustomComparer()
                {
                    var hashes = new ConstantHashProvider();
                    var values = new Hashtable(hashes, new PlainKeyComparer()) { [new PlainKey(7)] = 70 };
                    return (int)values[new PlainKey(7)]! + hashes.Calls;
                }

                private static int ValidateIncomparableFailure()
                {
                    var values = new Hashtable(new ConstantHashProvider(), null) { [new PlainKey(1)] = 10 };
                    try
                    {
                        _ = values[new PlainKey(1)];
                        return -1;
                    }
                    catch (ArgumentException exception)
                    {
                        return exception.Message.StartsWith("At least one object must implement IComparable.") ? 1 : -1;
                    }
                }

                private static int GrowHashtable()
                {
                    var values = new Hashtable(1);
                    for (int i = 0; i < 200; i++) values.Add(i, i * 3);
                    int checksum = 0;
                    for (int i = 0; i < 200; i++) checksum = unchecked(checksum * 31 + (int)values[i]!);
                    return unchecked(checksum + values.Count);
                }

                private static int RemoveAndReuseCollisions()
                {
                    var values = new Hashtable(new ConstantHashProvider(), new PlainKeyComparer());
                    for (int i = 0; i < 40; i++) values.Add(new PlainKey(i), i);
                    for (int i = 0; i < 40; i += 2) values.Remove(new PlainKey(i));
                    for (int i = 40; i < 60; i++) values.Add(new PlainKey(i), i);
                    int checksum = 0;
                    for (int i = 1; i < 60; i += 2)
                    {
                        if (values.ContainsKey(new PlainKey(i))) checksum = unchecked(checksum * 17 + (int)values[new PlainKey(i)]!);
                    }
                    return unchecked(checksum + values.Count);
                }

                private static int CloneLegacyComparerTable()
                {
                    var values = new Hashtable(new ConstantHashProvider(), new PlainKeyComparer())
                    {
                        [new PlainKey(3)] = 30,
                        [new PlainKey(4)] = 40,
                    };
                    var clone = (Hashtable)values.Clone();
                    return clone.Count == 2 && (int)clone[new PlainKey(3)]! == 30 && (int)clone[new PlainKey(4)]! == 40
                        ? clone.Count
                        : -1;
                }

                public static int Trace() => 0;
            }
            """,
            [0, 1, 2, 3, 4, 5, 6, 7, 8, 9])
        {
            CaptureCompilerDiagnostics = true,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });
}
