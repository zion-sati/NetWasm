using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

// This is an end-to-end differential acceptance fixture, not a unit test.
[Collection(CorrectnessTestGroup.Name)]
public sealed class FrozenOrdinalStringCompatibilityCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void FrozenOrdinalStringsMatchDesktopAcrossProfilesAndTargets() =>
        runner.Run(new(
            "FrozenOrdinalStringCompatibilityCorpus",
            "NetWasm.Correctness.FrozenOrdinalStrings",
            Source,
            [0, 1, 2, 3, 4, 5, 6])
        {
            ExpectedReturnValues = ImmutableDictionary<int, int>.Empty.Add(0, 8524).Add(6, 371),
            CaptureCompilerDiagnostics = true,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });

    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Collections.Frozen;

        namespace NetWasm.Correctness.FrozenOrdinalStrings;

        public static class EntryPoint
        {
            public static int Trace() => 0;

            public static int Run(int input) => input switch
            {
                0 => OrdinalDictionaryLookups(),
                1 => OrdinalIgnoreCaseDictionaryLookups(),
                2 => OrdinalSetLookups(),
                3 => OrdinalIgnoreCaseSetLookups(),
                4 => SubstringShapedDictionaryLookups(),
                5 => AlternateSpanLookups(),
                _ => EmptyAndFailureContracts(),
            };

            private static int OrdinalDictionaryLookups()
            {
                var pairs = new[]
                {
                    new KeyValuePair<string, int>("alpha-000", 3),
                    new KeyValuePair<string, int>("bravo-111", 5),
                    new KeyValuePair<string, int>("charlie-222", 7),
                    new KeyValuePair<string, int>("delta-333", 11),
                    new KeyValuePair<string, int>("echo-444", 13),
                    new KeyValuePair<string, int>("foxtrot-555", 17),
                    new KeyValuePair<string, int>("golf-666", 19),
                    new KeyValuePair<string, int>("hotel-777", 23),
                };
                var dictionary = pairs.ToFrozenDictionary(StringComparer.Ordinal);
                var total = dictionary.Count * 1000 + dictionary["alpha-000"] * 100;
                total += dictionary.TryGetValue("hotel-777", out var hotel) ? hotel : -100;
                total += dictionary.ContainsKey("ALPHA-000") ? 1000 : 0;
                foreach (var pair in dictionary)
                {
                    total += pair.Key.Length + pair.Value;
                }

                try
                {
                    _ = dictionary["missing"];
                    return -1;
                }
                catch (KeyNotFoundException)
                {
                    return total + 29;
                }
            }

            private static int OrdinalIgnoreCaseDictionaryLookups()
            {
                var pairs = new[]
                {
                    new KeyValuePair<string, int>("Alpha-000", 31),
                    new KeyValuePair<string, int>("BRAVO-111", 37),
                    new KeyValuePair<string, int>("Café-222", 41),
                    new KeyValuePair<string, int>("Delta-333", 43),
                    new KeyValuePair<string, int>("ECHO-444", 47),
                    new KeyValuePair<string, int>("Foxtrot-555", 53),
                    new KeyValuePair<string, int>("Golf-666", 59),
                    new KeyValuePair<string, int>("Hotel-777", 61),
                };
                var dictionary = pairs.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
                var total = dictionary["alpha-000"] + dictionary["bravo-111"] * 10;
                total += dictionary.TryGetValue("CAFÉ-222", out var cafe) ? cafe * 100 : -100;
                total += dictionary.Comparer.Equals("alpha-000", "ALPHA-000") ? 1000 : -1000;
                total += dictionary.ContainsKey("hotel-777") ? 10000 : 0;
                total += dictionary.ContainsKey("hotel-778") ? -10000 : 7;
                return total;
            }

            private static int OrdinalSetLookups()
            {
                var values = new[]
                {
                    "north-000", "south-111", "east-222", "west-333",
                    "north-444", "south-555", "east-666", "west-777",
                };
                var set = values.ToFrozenSet(StringComparer.Ordinal);
                var total = set.Count * 1000;
                total += set.Contains("north-000") ? 100 : 0;
                total += set.Contains("NORTH-000") ? -1000 : 11;
                total += set.TryGetValue("west-777", out var actual) && actual == "west-777" ? 1000 : -1000;
                total += set.TryGetValue("WEST-777", out _) ? -1000 : 13;
                foreach (var value in set)
                {
                    total += value.Length;
                }

                return total;
            }

            private static int OrdinalIgnoreCaseSetLookups()
            {
                var values = new[]
                {
                    "Alpha-000", "BRAVO-111", "Café-222", "DELTA-333",
                    "Echo-444", "FOXTROT-555", "Golf-666", "HOTEL-777",
                };
                var set = values.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
                var total = set.Count * 1000;
                total += set.Contains("alpha-000") ? 100 : -100;
                total += set.Contains("CAFÉ-222") ? 200 : -200;
                total += set.Contains("hotel-778") ? -1000 : 17;
                total += set.TryGetValue("bravo-111", out var actual) && actual == "BRAVO-111" ? 300 : -300;
                total += set.Comparer.Equals("alpha-000", "ALPHA-000") ? 19 : -19;
                return total;
            }

            private static int SubstringShapedDictionaryLookups()
            {
                var pairs = new[]
                {
                    new KeyValuePair<string, int>("prefix-0000-suffix", 2),
                    new KeyValuePair<string, int>("prefix-1111-suffix", 3),
                    new KeyValuePair<string, int>("prefix-2222-suffix", 5),
                    new KeyValuePair<string, int>("prefix-3333-suffix", 7),
                    new KeyValuePair<string, int>("prefix-4444-suffix", 11),
                    new KeyValuePair<string, int>("prefix-5555-suffix", 13),
                    new KeyValuePair<string, int>("prefix-6666-suffix", 17),
                    new KeyValuePair<string, int>("prefix-7777-suffix", 19),
                    new KeyValuePair<string, int>("prefix-8888-suffix", 23),
                    new KeyValuePair<string, int>("prefix-9999-suffix", 29),
                };
                var dictionary = pairs.ToFrozenDictionary(StringComparer.Ordinal);
                var total = dictionary["prefix-4444-suffix"] * 100;
                total += dictionary.ContainsKey("prefix-4445-suffix") ? -1000 : 31;
                total += dictionary.TryGetValue("prefix-8888-suffix", out var value) ? value : -100;
                return total + dictionary.Count;
            }

            private static int AlternateSpanLookups()
            {
                var pairs = new[]
                {
                    new KeyValuePair<string, int>("Alpha-0000-tail", 71),
                    new KeyValuePair<string, int>("Bravo-1111-tail", 73),
                    new KeyValuePair<string, int>("Café-2222-tail", 79),
                    new KeyValuePair<string, int>("Delta-3333-tail", 83),
                    new KeyValuePair<string, int>("Echo-4444-tail", 89),
                    new KeyValuePair<string, int>("Foxtrot-5555-tail", 97),
                    new KeyValuePair<string, int>("Golf-6666-tail", 101),
                    new KeyValuePair<string, int>("Hotel-7777-tail", 103),
                };
                var dictionary = pairs.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
                var lookup = dictionary.GetAlternateLookup<ReadOnlySpan<char>>();
                var total = lookup.ContainsKey("alpha-0000-tail".AsSpan()) ? 1000 : -1000;
                total += lookup.TryGetValue("CAFÉ-2222-TAIL".AsSpan(), out var cafe) ? cafe : -100;
                total += lookup.ContainsKey("missing".AsSpan()) ? -1000 : 107;

                var set = new[] { "left-0000-right", "left-1111-right", "left-2222-right", "left-3333-right", "left-4444-right", "left-5555-right" }
                    .ToFrozenSet(StringComparer.OrdinalIgnoreCase);
                var setLookup = set.GetAlternateLookup<ReadOnlySpan<char>>();
                total += setLookup.Contains("LEFT-3333-RIGHT".AsSpan()) ? 2000 : -2000;
                total += setLookup.TryGetValue("left-4444-right".AsSpan(), out var actual) && actual == "left-4444-right" ? 300 : -300;
                total += setLookup.Contains("left-9999-right".AsSpan()) ? -1000 : 109;
                return total;
            }

            private static int EmptyAndFailureContracts()
            {
                var dictionary = FrozenDictionary.Create<string, int>();
                var set = FrozenSet.Create<string>();
                var total = dictionary.Count * 100 + set.Count * 10;
                total += dictionary.TryGetValue("missing", out _) ? -100 : 113;
                total += set.Contains("missing") ? -100 : 127;
                try
                {
                    _ = dictionary["missing"];
                    return -1;
                }
                catch (KeyNotFoundException)
                {
                    return total + 131;
                }
            }
        }
        """;
}
