namespace NetWasm.Compiler.Tests.Correctness;

public sealed class DictionaryCompatibilityCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void DictionaryHelpersMatchDesktopAcrossCompilerProfilesAndTargets() =>
        runner.Run(new(
            "DictionaryCompatibilityCorpus",
            "NetWasm.Correctness.Dictionaries",
            """
            using System;
            using System.Collections;
            using System.Collections.Generic;
            using System.Numerics;

            namespace NetWasm.Correctness.Dictionaries;

            public static class EntryPoint
            {
                private static readonly int[] CollisionSeeds = new[]
                {
                    0xE020, 0xE060, 0xE0A0, 0xE0E0, 0xE120, 0xE160, 0xE1A0, 0xE1E0,
                    0xE220, 0xE260, 0xE2A0, 0xE2E0, 0xE320, 0xE360, 0xE3A0, 0xE3E0,
                    0xE420, 0xE460, 0xE4A0, 0xE4E0, 0xE520, 0xE560, 0xE5A0, 0xE5E0,
                    0xE620, 0xE660, 0xE6A0, 0xE6E0, 0xE720, 0xE7A0, 0xE7E0, 0xE820,
                    0xE860, 0xE8A0, 0xE8E0, 0xE920, 0xE960, 0xE9A0, 0xE9E0, 0xEA20,
                    0xEA60, 0xEAA0, 0xEAE0, 0xEB20, 0xEB60, 0xEBE0, 0xEC20, 0xEC60,
                    0xECA0, 0xECE0, 0xED20, 0xED60, 0xEDA0, 0xEDE0, 0xEE20, 0xEE60,
                    0xEEA0, 0xEEE0, 0xEF60, 0xEFA0, 0xEFE0, 0xF020, 0xF060, 0xF0A0,
                    0xF0E0, 0xF120, 0xF160, 0xF1A0, 0xF1E0, 0xF220, 0xF260, 0xF2A0,
                    0xF2E0, 0xF320, 0xF360, 0xF3A0, 0xF3E0, 0xF420, 0xF460, 0xF4A0,
                    0xF4E0, 0xF520, 0xF560, 0xF5A0, 0xF5E0, 0xF620, 0xF660, 0xF6A0,
                    0xF6E0, 0xF720, 0xF760, 0xF7A0, 0xF7E0, 0xF820, 0xF860, 0xF8A0,
                    0xF8E0, 0xF920, 0xF960, 0xF9A0, 0xF9E0, 0xFA20, 0xFA60, 0xFAA0,
                    0xFAE0, 0xFB60, 0xFBA0, 0xFBE0, 0xFC20, 0xFC60,
                };

                public static int Run(int input) => input switch
                {
                    0 => ComparerIdentity(),
                    1 => GenericEmptyEnumeration(),
                    2 => ArrayBackedEmptyEnumeration(),
                    3 => CollisionUpgrade(StringComparer.Ordinal, false),
                    _ => CollisionUpgrade(StringComparer.OrdinalIgnoreCase, true),
                };

                private static int ComparerIdentity()
                {
                    var defaultDictionary = new Dictionary<string, int>();
                    var ordinal = new Dictionary<string, int>(StringComparer.Ordinal);
                    var ignoreCase = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    return ReferenceEquals(defaultDictionary.Comparer, EqualityComparer<string>.Default)
                        && ReferenceEquals(ordinal.Comparer, StringComparer.Ordinal)
                        && ReferenceEquals(ignoreCase.Comparer, StringComparer.OrdinalIgnoreCase)
                        ? 111
                        : -1;
                }

                private static int GenericEmptyEnumeration()
                {
                    IEnumerable<KeyValuePair<int, int>> values = new Dictionary<int, int>();
                    var enumerator = values.GetEnumerator();
                    var score = CurrentState(enumerator, "Enumeration has not started. Call MoveNext.") ? 1 : 0;
                    score += !enumerator.MoveNext() ? 10 : 0;
                    score += CurrentState(enumerator, "Enumeration has not started. Call MoveNext.") ? 100 : 0;
                    enumerator.Reset();
                    score += CurrentState(enumerator, "Enumeration has not started. Call MoveNext.") ? 1000 : 0;
                    enumerator.Dispose();
                    return score;
                }

                private static int ArrayBackedEmptyEnumeration()
                {
                    var dictionary = new Dictionary<int, int>();
                    var keyEnumerator = dictionary.Keys.GetEnumerator();
                    var valueEnumerator = dictionary.Values.GetEnumerator();
                    var arrayEnumerator = ((IEnumerable<int>)Array.Empty<int>()).GetEnumerator();
                    var score = ExerciseArrayEnumerator(keyEnumerator) ? 1 : 0;
                    score += ExerciseArrayEnumerator(valueEnumerator) ? 10 : 0;
                    score += ExerciseArrayEnumerator(arrayEnumerator) ? 100 : 0;
                    return score;
                }

                private static bool ExerciseArrayEnumerator<T>(IEnumerator<T> enumerator)
                {
                    if (!CurrentState(enumerator, "Enumeration has not started. Call MoveNext.")) return false;
                    if (enumerator.MoveNext()) return false;
                    if (!CurrentState(enumerator, "Enumeration already finished.")) return false;
                    enumerator.Reset();
                    if (!CurrentState(enumerator, "Enumeration has not started. Call MoveNext.")) return false;
                    enumerator.Dispose();
                    return true;
                }

                private static bool CurrentState<T>(IEnumerator<T> enumerator, string expected)
                {
                    try
                    {
                        _ = enumerator.Current;
                        return false;
                    }
                    catch (InvalidOperationException exception)
                    {
                        return exception.Message == expected;
                    }
                }

                private static int CollisionUpgrade(IEqualityComparer<string> comparer, bool ignoreCase)
                {
                    var dictionary = new Dictionary<string, int>(CollisionSeeds.Length + 4, comparer);
                    for (var index = 0; index < CollisionSeeds.Length; index++)
                    {
                        dictionary.Add(CreateCollision(CollisionSeeds[index]), index);
                    }

                    var lookup = dictionary.GetAlternateLookup<ReadOnlySpan<char>>();
                    for (var index = 0; index < CollisionSeeds.Length; index += 11)
                    {
                        var key = CreateCollision(CollisionSeeds[index]);
                        if (!dictionary.TryGetValue(key, out var value) || value != index) return -1;
                        if (!lookup.TryGetValue(key.AsSpan(), out value) || value != index) return -2;
                    }

                    if (!ReferenceEquals(dictionary.Comparer, comparer)) return -3;

                    var lowerAscii = Repeat("alpha-bravo-", 8);
                    var upperAscii = lowerAscii.ToUpperInvariant();
                    dictionary.Add(lowerAscii, 701);
                    if (ignoreCase && dictionary[upperAscii] != 701) return -4;

                    var lowerUnicode = Repeat("café-ω-", 12);
                    var upperUnicode = lowerUnicode.ToUpperInvariant();
                    dictionary.Add(lowerUnicode, 907);
                    if (ignoreCase &&
                        (!dictionary.TryGetValue(upperUnicode, out var unicodeValue) || unicodeValue != 907 ||
                         !lookup.TryGetValue(upperUnicode.AsSpan(), out unicodeValue) || unicodeValue != 907))
                    {
                        return -5;
                    }

                    return dictionary.Count * 100 + dictionary[CreateCollision(CollisionSeeds[109])];
                }

                private static string CreateCollision(int seed)
                {
                    uint hash1 = (5381u << 16) + 5381u;
                    var hash2 = BitOperations.RotateLeft(hash1, 5) + hash1;
                    hash1 = (BitOperations.RotateLeft(hash1, 5) + hash1) ^ (uint)seed;
                    hash1 = BitOperations.RotateLeft(hash1, 5) + hash1;
                    return new string(new[]
                    {
                        (char)seed,
                        '\0',
                        (char)hash2,
                        (char)(hash2 >> 16),
                        (char)hash1,
                        (char)(hash1 >> 16),
                        '\0',
                        '\0',
                    });
                }

                private static string Repeat(string value, int count)
                {
                    var result = string.Empty;
                    for (var index = 0; index < count; index++) result += value;
                    return result;
                }

                public static int Trace() => 0;
            }
            """,
            [0, 1, 2, 3, 4])
        {
            CaptureCompilerDiagnostics = true,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });
}
