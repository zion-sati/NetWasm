namespace NetWasm.Compiler.Tests.Correctness;

// This is an end-to-end differential acceptance fixture, not a unit test.
public sealed class ImmutableDictionaryCompatibilityCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void ImmutableDictionariesMatchDesktopForEveryCompilerProfileAndTarget() =>
        runner.Run(new(
            "ImmutableDictionaryCompatibilityCorpus",
            "NetWasm.Correctness.ImmutableDictionaries",
            Source,
            [0, 1, 2, 3, 4, 5, 6, 7, 8, 9])
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

        namespace NetWasm.Correctness.ImmutableDictionaries;

        public static class EntryPoint
        {
            public static int Trace() => 0;

            public static int Run(int input) => input switch
            {
                0 => PersistentOperations(),
                1 => ComparerAndCanonicalKeyOperations(),
                2 => BuilderSnapshots(),
                3 => InterfaceViewsAndEnumeration(),
                4 => FailureContracts(),
                5 => ExtensionFactories(),
                6 => CopiedEnumeratorDisposal(),
                7 => EmptyInterfaceEnumeration(),
                8 => BuilderSyncRootAndMissingValue(),
                _ => BuilderEnumeratorInvalidation(),
            };

            private static int PersistentOperations()
            {
                var original = ImmutableDictionary.CreateRange(new[]
                {
                    new KeyValuePair<string, int>("a", 1),
                    new KeyValuePair<string, int>("b", 2),
                });
                var changed = original.Add("c", 3).SetItem("a", 4).Remove("b");
                return original.Count * 1000 + original["a"] * 100 +
                    changed.Count * 10 + changed["a"] +
                    (changed.ContainsKey("b") ? 1 : 0);
            }

            private static int ComparerAndCanonicalKeyOperations()
            {
                var map = ImmutableDictionary.Create<string, int>(StringComparer.OrdinalIgnoreCase)
                    .Add("Key", 7);
                var same = map.Add("key", 7);
                var changed = same.SetItem("KEY", 8);
                var found = changed.TryGetKey("key", out var actualKey);
                return (ReferenceEquals(map, same) ? 1000 : 0) +
                    (found ? 100 : 0) +
                    (actualKey == "Key" ? 10 : 0) + changed["key"];
            }

            private static int BuilderSnapshots()
            {
                var builder = ImmutableDictionary.CreateBuilder<string, int>();
                builder.Add("one", 1);
                builder["two"] = 2;
                var first = builder.ToImmutable();
                builder["one"] = 9;
                builder.Remove("two");
                var second = builder.ToImmutable();
                return first.Count * 1000 + first["one"] * 100 +
                    second.Count * 10 + second["one"];
            }

            private static int InterfaceViewsAndEnumeration()
            {
                IImmutableDictionary<int, string> immutable =
                    ImmutableDictionary.CreateRange(new[]
                    {
                        new KeyValuePair<int, string>(1, "one"),
                        new KeyValuePair<int, string>(2, "two"),
                        new KeyValuePair<int, string>(3, "three"),
                    });
                var readOnly = (IReadOnlyDictionary<int, string>)immutable;
                var dictionary = (IDictionary<int, string>)immutable;
                var sum = 0;
                foreach (var pair in immutable)
                {
                    sum += pair.Key;
                }

                var keys = dictionary.Keys;
                var values = dictionary.Values;
                return sum * 1000 + readOnly.Count * 100 + keys.Count * 10 +
                    (keys.Contains(2) ? 1 : 0) + (values.Contains("three") ? 2 : 0);
            }

            private static int FailureContracts()
            {
                var map = ImmutableDictionary.Create<string, int>().Add("a", 1);
                var failures = 0;
                try { _ = map.Add("a", 2); } catch (ArgumentException) { failures++; }
                try { _ = map["missing"]; } catch (KeyNotFoundException) { failures++; }
                try { _ = ImmutableDictionary.Create<string, int>().Add(null!, 1); } catch (ArgumentNullException) { failures++; }
                try { ((ICollection<KeyValuePair<string, int>>)map).Add(new("b", 2)); } catch (NotSupportedException) { failures++; }
                return failures;
            }

            private static int ExtensionFactories()
            {
                var source = new[] { 2, 4, 6 };
                var map = source.ToImmutableDictionary(value => value, value => value * 10);
                var overwritten = ImmutableDictionary.CreateRangeWithOverwrite(
                    new KeyValuePair<int, int>(1, 2),
                    new KeyValuePair<int, int>(1, 9));
                return map.Count * 1000 + map[4] * 10 + overwritten[1];
            }

            private static int CopiedEnumeratorDisposal()
            {
                var map = ImmutableDictionary.Create<int, int>()
                    .Add(1, 10)
                    .Add(2, 20);
                var first = map.GetEnumerator();
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
                IEnumerable<KeyValuePair<int, int>> values = ImmutableDictionary<int, int>.Empty;
                using var enumerator = values.GetEnumerator();
                var before = enumerator.MoveNext();
                enumerator.Reset();
                var after = enumerator.MoveNext();
                return !before && !after ? 1 : 0;
            }

            private static int BuilderSyncRootAndMissingValue()
            {
                var builder = ImmutableDictionary.CreateBuilder<string, string>();
                builder.Add("present", "value");
                ICollection collection = builder;
                var syncRoot = collection.SyncRoot;
                var missing = builder.TryGetValue("missing", out var value);
                return (ReferenceEquals(syncRoot, collection.SyncRoot) ? 100 : 0) +
                    (!missing ? 10 : 0) + (value is null ? 1 : 0);
            }

            private static int BuilderEnumeratorInvalidation()
            {
                var builder = ImmutableDictionary.CreateBuilder<int, int>();
                builder.Add(1, 10);
                var enumerator = builder.GetEnumerator();
                enumerator.MoveNext();
                builder.Add(2, 20);
                try
                {
                    enumerator.MoveNext();
                    return 0;
                }
                catch (InvalidOperationException)
                {
                    return 1;
                }
            }
        }
        """;
}
