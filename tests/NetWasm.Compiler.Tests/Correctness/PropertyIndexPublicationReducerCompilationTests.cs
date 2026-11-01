namespace NetWasm.Compiler.Tests.Correctness;

public sealed class PropertyIndexPublicationReducerCompilationTests(
    CorrectnessTestRunner runner)
{
    [Fact]
    public void PublishedMetadataEntriesKeepCacheIdentityAndAssignedIndexes() =>
        runner.Run(new(
            "PropertyIndexPublicationReducerCompilation",
            "NetWasm.Correctness.PropertyIndexPublicationReducer",
            Source,
            [0, 1, 2, 3])
        {
            CaptureCompilerDiagnostics = true,
            ExposesLegacyTrace = false,
        });

    private const string Source = """
        using System;
        using System.Collections.Generic;

        namespace NetWasm.Correctness.PropertyIndexPublicationReducer;

        public abstract class MetadataEntry
        {
            protected MetadataEntry(string name, int value)
            {
                Name = name;
                Value = value;
            }

            public string Name { get; }

            public int Value { get; }

            public int PropertyIndex { get; set; }
        }

        public sealed class FirstEntry : MetadataEntry
        {
            public FirstEntry() : base("first", 11)
            {
            }
        }

        public sealed class SecondEntry : MetadataEntry
        {
            public SecondEntry() : base("second", 22)
            {
            }
        }

        public sealed class ThirdEntry : MetadataEntry
        {
            public ThirdEntry() : base("third", 33)
            {
            }
        }

        public sealed class AssignedPropertySet
        {
            private readonly int[] _storage;

            public AssignedPropertySet(int length)
            {
                _storage = new int[length];
            }

            public bool this[int index]
            {
                get => _storage[index] != 0;
                set => _storage[index] = value ? 1 : 0;
            }
        }

        public sealed class MetadataCatalog
        {
            private MetadataEntry[]? _entries;
            private Dictionary<string, MetadataEntry>? _propertyIndex;
            private bool _configured;

            public MetadataEntry[] Entries
            {
                get
                {
                    EnsureConfigured();
                    return _entries!;
                }
            }

            public Dictionary<string, MetadataEntry> PropertyIndex
            {
                get
                {
                    EnsureConfigured();
                    return _propertyIndex!;
                }
            }

            private void EnsureConfigured()
            {
                if (_configured)
                {
                    return;
                }

                var entries = new List<MetadataEntry>
                {
                    new FirstEntry(),
                    new SecondEntry(),
                    new ThirdEntry(),
                };

                for (var index = 0; index < entries.Count; index++)
                {
                    entries[index].PropertyIndex = index;
                }

                var propertyIndex =
                    new Dictionary<string, MetadataEntry>(StringComparer.Ordinal);
                foreach (var entry in entries)
                {
                    propertyIndex.Add(entry.Name, entry);
                }

                _entries = entries.ToArray();
                _propertyIndex = propertyIndex;
                _configured = true;
            }
        }

        public static class GenericConsumer<TEntry>
            where TEntry : MetadataEntry
        {
            public static int MarkKnownEntry(
                MetadataCatalog catalog,
                string key,
                AssignedPropertySet assigned)
            {
                var entry = (TEntry)catalog.PropertyIndex[key];
                assigned[entry.PropertyIndex] = true;
                return entry.Value;
            }
        }

        public static class EntryPoint
        {
            private static readonly MetadataCatalog Catalog = new();

            public static int Run(int input) => input switch
            {
                0 => VerifyFieldPublication(),
                1 => VerifyCacheLookupIdentity(),
                2 => VerifyIndexRange(),
                3 => VerifyBitsetWrite(),
                _ => -1,
            };

            private static int VerifyFieldPublication()
            {
                var entries = Catalog.Entries;
                var propertyIndex = Catalog.PropertyIndex;
                return entries.Length == 3 &&
                    propertyIndex.Count == 3 &&
                    ReferenceEquals(entries[0], propertyIndex["first"]) &&
                    ReferenceEquals(entries[1], propertyIndex["second"]) &&
                    ReferenceEquals(entries[2], propertyIndex["third"])
                    ? 101
                    : -1;
            }

            private static int VerifyCacheLookupIdentity()
            {
                var entries = Catalog.Entries;
                var propertyIndex = Catalog.PropertyIndex;
                var first = propertyIndex["first"];
                var second = propertyIndex["second"];
                var third = propertyIndex["third"];
                return ReferenceEquals(first, entries[0]) &&
                    ReferenceEquals(second, entries[1]) &&
                    ReferenceEquals(third, entries[2]) &&
                    first.Name == "first" &&
                    second.Name == "second" &&
                    third.Name == "third"
                    ? 211
                    : -1;
            }

            private static int VerifyIndexRange()
            {
                var entries = Catalog.Entries;
                return entries[0].PropertyIndex == 0 &&
                    entries[1].PropertyIndex == 1 &&
                    entries[2].PropertyIndex == 2 &&
                    entries[0].PropertyIndex >= 0 &&
                    entries[2].PropertyIndex < entries.Length
                    ? 307
                    : -1;
            }

            private static int VerifyBitsetWrite()
            {
                var assigned = new AssignedPropertySet(3);
                var value = GenericConsumer<ThirdEntry>.MarkKnownEntry(
                    Catalog,
                    "third",
                    assigned);
                return value == 33 &&
                    !assigned[0] &&
                    !assigned[1] &&
                    assigned[2]
                    ? 413
                    : -1;
            }
        }
        """;
}
