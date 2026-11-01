namespace NetWasm.Compiler.Tests.Correctness;

// This is an end-to-end differential acceptance fixture, not a unit test.
public sealed class FrozenDictionaryCompatibilityCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void FrozenDictionariesMatchDesktopForEveryCompilerProfileAndTarget() =>
        runner.Run(new(
            "FrozenDictionaryCompatibilityCorpus",
            "NetWasm.Correctness.FrozenDictionaries",
            Source,
            [0, 1, 2, 3, 4, 5, 6, 7])
        {
            CaptureCompilerDiagnostics = true,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });

    private const string Source = """
        using System;
        using System.Collections;
        using System.Collections.Generic;
        using System.Collections.Frozen;

        namespace NetWasm.Correctness.FrozenDictionaries;

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
                2 => IntegralSpecializationOperations(),
                3 => StringAndAlternateLookupOperations(),
                4 => InterfaceViewsAndFailures(),
                5 => ExtensionFactoryOperations(),
                6 => EnumKeyOperations(),
                _ => ProjectionFailureOperations(),
            };

            private static int EmptyAndFactoryOperations()
            {
                var empty = FrozenDictionary<string, int>.Empty;
                var custom = FrozenDictionary.Create<string, int>(
                    StringComparer.OrdinalIgnoreCase,
                    new KeyValuePair<string, int>("one", 1));
                return empty.Count * 1000 +
                    (empty.ContainsKey("missing") ? 1 : 0) * 100 +
                    custom.Count * 10 + custom["ONE"];
            }

            private static int DuplicateAndComparerOperations()
            {
                var map = FrozenDictionary.Create<string, int>(
                    StringComparer.OrdinalIgnoreCase,
                    new KeyValuePair<string, int>("Key", 1),
                    new KeyValuePair<string, int>("key", 7),
                    new KeyValuePair<string, int>("Other", 3));
                var ordinal = FrozenDictionary.Create<string, int>(
                    StringComparer.Ordinal,
                    new KeyValuePair<string, int>("Key", 2),
                    new KeyValuePair<string, int>("key", 5));
                return map.Count * 1000 + map["KEY"] * 10 +
                    ordinal.Count * 100 + ordinal["key"];
            }

            private static int IntegralSpecializationOperations()
            {
                var pairs = new KeyValuePair<int, int>[12];
                for (var i = 0; i < pairs.Length; i++)
                {
                    pairs[i] = new KeyValuePair<int, int>(i, i * 10);
                }

                var map = FrozenDictionary.Create<int, int>(pairs);
                return map.Count * 1000 + map[0] + map[11] +
                    (map.ContainsKey(12) ? 1 : 0);
            }

            private static int StringAndAlternateLookupOperations()
            {
                var map = FrozenDictionary.Create<string, int>(
                    StringComparer.OrdinalIgnoreCase,
                    new KeyValuePair<string, int>("red", 1),
                    new KeyValuePair<string, int>("blue", 2),
                    new KeyValuePair<string, int>("green", 3),
                    new KeyValuePair<string, int>("white", 4),
                    new KeyValuePair<string, int>("black", 5),
                    new KeyValuePair<string, int>("brown", 6));

                var lookup = map.GetAlternateLookup<ReadOnlySpan<char>>();
                ReadOnlySpan<char> query = "BLUE";
                var found = lookup.TryGetValue(query, out var value);
                return map.Count * 100 + (found ? 10 : 0) + value +
                    (lookup.ContainsKey("missing") ? 1 : 0);
            }

            private static int InterfaceViewsAndFailures()
            {
                var map = FrozenDictionary.Create(
                    new KeyValuePair<int, string>(1, "one"),
                    new KeyValuePair<int, string>(2, "two"),
                    new KeyValuePair<int, string>(3, "three"));
                var readOnly = (IReadOnlyDictionary<int, string>)map;
                var dictionary = (IDictionary<int, string>)map;
                var sum = 0;
                foreach (var pair in readOnly)
                {
                    sum += pair.Key;
                }

                var copied = new KeyValuePair<int, string>[3];
                map.CopyTo(copied, 0);
                var failures = 0;
                try { _ = map[99]; } catch (KeyNotFoundException) { failures++; }
                try { dictionary.Add(4, "four"); } catch (NotSupportedException) { failures++; }
                try { map.CopyTo(new KeyValuePair<int, string>[2], 0); } catch (ArgumentException) { failures++; }
                return sum * 1000 + readOnly.Count * 100 +
                    (copied[0].Key + copied[1].Key + copied[2].Key) * 10 + failures;
            }

            private static int ExtensionFactoryOperations()
            {
                IEnumerable<KeyValuePair<int, int>> pairs = new[]
                {
                    new KeyValuePair<int, int>(1, 10),
                    new KeyValuePair<int, int>(2, 20),
                    new KeyValuePair<int, int>(3, 30),
                };
                var map = pairs.ToFrozenDictionary();
                var projected = new[] { 4, 5, 6 }.ToFrozenDictionary(value => value, value => value * 2);
                return map.Count * 1000 + map[2] + projected.Count * 100 + projected[6];
            }

            private static int EnumKeyOperations()
            {
                var map = FrozenDictionary.Create(
                    new KeyValuePair<SmallCode, int>(SmallCode.Zero, 10),
                    new KeyValuePair<SmallCode, int>(SmallCode.Two, 20),
                    new KeyValuePair<SmallCode, int>(SmallCode.Three, 30));
                return map.Count * 1000 + map[SmallCode.Two] +
                    (map.ContainsKey(SmallCode.One) ? 1 : 0);
            }

            private static int ProjectionFailureOperations()
            {
                IEnumerable<int> source = new[] { 1, 1 };
                var failures = 0;
                try
                {
                    _ = source.ToFrozenDictionary(value => value);
                }
                catch (ArgumentException)
                {
                    failures++;
                }

                try
                {
                    _ = source.ToFrozenDictionary<int, int>(null!);
                }
                catch (ArgumentNullException)
                {
                    failures++;
                }

                try
                {
                    _ = source.ToFrozenDictionary(value => value, (Func<int, int>)null!);
                }
                catch (ArgumentNullException)
                {
                    failures++;
                }

                return failures;
            }
        }
        """;
}
