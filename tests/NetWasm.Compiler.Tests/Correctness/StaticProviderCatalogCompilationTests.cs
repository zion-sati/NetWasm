using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

[Collection(CorrectnessTestGroup.Name)]
public sealed class StaticProviderCatalogCompilationTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void StaticProviderCatalogPreservesAllReferenceEntries() =>
        runner.Run(new(
            "StaticProviderCatalogCompilation",
            "NetWasm.Correctness.StaticProviderCatalog",
            Source,
            [0, 1, 2, 3, 4, 5, 6, 7])
        {
            ExpectedReturnValues = ImmutableDictionary<int, int>.Empty
                .Add(0, 914).Add(1, 4914).Add(2, 914).Add(3, 914).Add(4, 914)
                .Add(5, 3278).Add(6, 8894).Add(7, 10420),
            CaptureCompilerDiagnostics = true,
        });

    private const string Source = """
        using System;
        using System.Collections.Generic;

        namespace NetWasm.Correctness.StaticProviderCatalog;

        public sealed class Entry
        {
            public Entry(int value, string provider) =>
                (Value, Provider, Marker) = (value, provider, null);

            public Entry(int value, string provider, object marker) =>
                (Value, Provider, Marker) = (value, provider, marker);

            public int Value { get; }

            public string Provider { get; }

            public object? Marker { get; }
        }

        public static class ProviderA
        {
            public static readonly Entry[] Entries =
            [
                new Entry(1, "A"),
                new Entry(2, "A"),
                new Entry(3, "A"),
                new Entry(4, "A"),
                new Entry(5, "A"),
                new Entry(6, "A"),
                new Entry(7, "A"),
                new Entry(8, "A"),
                new Entry(9, "A"),
                new Entry(10, "A"),
                new Entry(11, "A"),
                new Entry(12, "A"),
            ];
        }

        public static class ProviderB
        {
            public static readonly Entry[] Entries =
            [
                new Entry(101, "B"),
                new Entry(102, "B"),
                new Entry(103, "B"),
                new Entry(104, "B"),
                new Entry(105, "B"),
                new Entry(106, "B"),
                new Entry(107, "B"),
                new Entry(108, "B"),
            ];
        }

        public static class Catalog
        {
            public static readonly List<Entry> Entries =
                [..ProviderA.Entries, ..ProviderB.Entries];
        }

        public static class DirectArrayCatalog
        {
            public static readonly Entry[] Entries =
                [..ProviderA.Entries, ..ProviderB.Entries];
        }

        public static class CopyCatalog
        {
            public static readonly Entry[] Entries = CreateEntries();

            private static Entry[] CreateEntries()
            {
                var entries = new Entry[
                    ProviderA.Entries.Length + ProviderB.Entries.Length];
                var index = 0;
                foreach (var entry in ProviderA.Entries)
                {
                    entries[index++] = entry;
                }
                foreach (var entry in ProviderB.Entries)
                {
                    entries[index++] = entry;
                }
                return entries;
            }
        }

        public static class ListCatalog
        {
            public static readonly List<Entry> Entries = CreateEntries();

            private static List<Entry> CreateEntries()
            {
                var entries = new List<Entry>();
                entries.AddRange(ProviderA.Entries);
                entries.AddRange(ProviderB.Entries);
                return entries;
            }
        }

        public abstract class Marker
        {
            public abstract int BaseValue { get; }

            public abstract int Count { get; }

            public abstract string Provider { get; }
        }

        public sealed class MarkerA : Marker
        {
            public override int BaseValue => 201;

            public override int Count => 12;

            public override string Provider => "A";
        }

        public sealed class MarkerB : Marker
        {
            public override int BaseValue => 301;

            public override int Count => 8;

            public override string Provider => "B";
        }

        public static class GenericProvider<TMarker>
            where TMarker : Marker, new()
        {
            public static readonly TMarker Marker = new TMarker();

            public static readonly Entry[] Entries = CreateEntries();

            private static Entry[] CreateEntries()
            {
                var entries = new Entry[Marker.Count];
                for (var index = 0; index < entries.Length; index++)
                {
                    entries[index] = new Entry(
                        Marker.BaseValue + index,
                        Marker.Provider,
                        Marker);
                }
                return entries;
            }
        }

        public static class GenericCatalog
        {
            public static readonly List<Entry> Entries =
                [..GenericProvider<MarkerA>.Entries,
                    ..GenericProvider<MarkerB>.Entries];
        }

        public abstract class CatalogCase
        {
            protected CatalogCase(string identity, string provider, int content) =>
                (Identity, Provider, Content) = (identity, provider, content);

            public string Identity { get; }

            public string Provider { get; }

            public int Content { get; }
        }

        public sealed class ClosedCase<TValue> : CatalogCase
        {
            public ClosedCase(
                string identity,
                string provider,
                int content,
                TValue value) : base(identity, provider, content)
            {
                Value = value;
            }

            public ClosedCase(
                string identity,
                string provider,
                int content,
                TValue value,
                Func<TValue> creator,
                Func<TValue, int> invoker) : base(identity, provider, content)
            {
                Value = value;
                Creator = creator;
                Invoker = invoker;
            }

            public TValue Value { get; }

            public Func<TValue>? Creator { get; }

            public Func<TValue, int>? Invoker { get; }
        }

        public static class CaseProviderA
        {
            public static CatalogCase[] CreateCases()
            {
                var cases = new List<CatalogCase>();
                cases.Add(new ClosedCase<int>("A00", "A", 100, 100));
                cases.Add(new ClosedCase<int>("A01", "A", 101, 101));
                cases.Add(new ClosedCase<int>("A02", "A", 102, 102));
                cases.Add(new ClosedCase<int>("A03", "A", 103, 103));
                cases.Add(new ClosedCase<int>("A04", "A", 104, 104));
                cases.Add(new ClosedCase<int>("A05", "A", 105, 105));
                cases.Add(new ClosedCase<int>("A06", "A", 106, 106));
                cases.Add(new ClosedCase<int>("A07", "A", 107, 107));
                cases.Add(new ClosedCase<int>("A08", "A", 108, 108));
                cases.Add(new ClosedCase<int>("A09", "A", 109, 109));
                cases.Add(new ClosedCase<int>("A10", "A", 110, 110));
                cases.Add(new ClosedCase<int>("A11", "A", 111, 111));
                return cases.ToArray();
            }
        }

        public static class CaseProviderB
        {
            public static CatalogCase[] CreateCases()
            {
                var cases = new List<CatalogCase>();
                cases.Add(new ClosedCase<int>("B00", "B", 200, 200));
                cases.Add(new ClosedCase<int>("B01", "B", 201, 201));
                cases.Add(new ClosedCase<int>("B02", "B", 202, 202));
                cases.Add(new ClosedCase<int>("B03", "B", 203, 203));
                return cases.ToArray();
            }
        }

        public static class CaseProviderC
        {
            public static CatalogCase[] CreateCases()
            {
                var cases = new List<CatalogCase>();
                cases.Add(new ClosedCase<int>("C00", "C", 300, 300));
                cases.Add(new ClosedCase<int>("C01", "C", 301, 301));
                cases.Add(new ClosedCase<int>("C02", "C", 302, 302));
                cases.Add(new ClosedCase<string>(
                    "C03",
                    "C",
                    303,
                    "text",
                    CreateText,
                    MeasureText));
                return cases.ToArray();
            }

            private static string CreateText() => "created";

            private static int MeasureText(string value) => value.Length;
        }

        public static class CachedCatalog
        {
            private static readonly CatalogCase[] CachedEntries = CreateEntries();

            public static IReadOnlyList<CatalogCase> Entries => CachedEntries;

            private static CatalogCase[] CreateEntries()
            {
                var entries = new List<CatalogCase>();
                entries.AddRange(CaseProviderA.CreateCases());
                entries.AddRange(CaseProviderB.CreateCases());
                entries.AddRange(CaseProviderC.CreateCases());
                return entries.ToArray();
            }
        }

        public static class CapacityProviderA
        {
            public static CatalogCase[] CreateCases()
            {
                var cases = new List<CatalogCase>();
                cases.Add(new ClosedCase<int>("G-A00", "GA", 400, 400));
                cases.Add(new ClosedCase<int>("G-A01", "GA", 401, 401));
                cases.Add(new ClosedCase<int>("G-A02", "GA", 402, 402));
                cases.Add(new ClosedCase<int>("G-A03", "GA", 403, 403));
                cases.Add(new ClosedCase<int>("G-A04", "GA", 404, 404));
                cases.Add(new ClosedCase<int>("G-A05", "GA", 405, 405));
                cases.Add(new ClosedCase<int>("G-A06", "GA", 406, 406));
                cases.Add(new ClosedCase<int>("G-A07", "GA", 407, 407));
                cases.Add(new ClosedCase<int>("G-A08", "GA", 408, 408));
                cases.Add(new ClosedCase<int>("G-A09", "GA", 409, 409));
                cases.Add(new ClosedCase<int>("G-A10", "GA", 410, 410));
                cases.Add(new ClosedCase<int>("G-A11", "GA", 411, 411));
                return cases.ToArray();
            }
        }

        public static class CapacityProviderB
        {
            public static CatalogCase[] CreateCases()
            {
                var cases = new List<CatalogCase>();
                cases.Add(new ClosedCase<int>("G-B00", "GB", 500, 500));
                cases.Add(new ClosedCase<int>("G-B01", "GB", 501, 501));
                cases.Add(new ClosedCase<int>("G-B02", "GB", 502, 502));
                cases.Add(new ClosedCase<int>("G-B03", "GB", 503, 503));
                cases.Add(new ClosedCase<int>("G-B04", "GB", 504, 504));
                cases.Add(new ClosedCase<int>("G-B05", "GB", 505, 505));
                cases.Add(new ClosedCase<int>("G-B06", "GB", 506, 506));
                cases.Add(new ClosedCase<string>(
                    "G-B07",
                    "GB",
                    507,
                    "capacity",
                    CreateText,
                    MeasureText));
                return cases.ToArray();
            }

            private static string CreateText() => "created";

            private static int MeasureText(string value) => value.Length;
        }

        public static class CapacityCatalog
        {
            private static readonly CatalogCase[] CachedEntries = CreateEntries();

            public static IReadOnlyList<CatalogCase> Entries => CachedEntries;

            private static CatalogCase[] CreateEntries()
            {
                var entries = new List<CatalogCase>();
                entries.AddRange(CapacityProviderA.CreateCases());
                entries.AddRange(CapacityProviderB.CreateCases());
                return entries.ToArray();
            }
        }

        public static class BoundaryProviderA
        {
            public static CatalogCase[] CreateCases()
            {
                var cases = new List<CatalogCase>();
                cases.Add(new ClosedCase<int>("B-A00", "BA", 600, 600));
                cases.Add(new ClosedCase<int>("B-A01", "BA", 601, 601));
                cases.Add(new ClosedCase<int>("B-A02", "BA", 602, 602));
                cases.Add(new ClosedCase<int>("B-A03", "BA", 603, 603));
                cases.Add(new ClosedCase<int>("B-A04", "BA", 604, 604));
                cases.Add(new ClosedCase<int>("B-A05", "BA", 605, 605));
                cases.Add(new ClosedCase<int>("B-A06", "BA", 606, 606));
                cases.Add(new ClosedCase<int>("B-A07", "BA", 607, 607));
                cases.Add(new ClosedCase<int>("B-A08", "BA", 608, 608));
                cases.Add(new ClosedCase<int>("B-A09", "BA", 609, 609));
                cases.Add(new ClosedCase<int>("B-A10", "BA", 610, 610));
                cases.Add(new ClosedCase<int>("B-A11", "BA", 611, 611));
                cases.Add(new ClosedCase<int>("B-A12", "BA", 612, 612));
                cases.Add(new ClosedCase<int>("B-A13", "BA", 613, 613));
                cases.Add(new ClosedCase<int>("B-A14", "BA", 614, 614));
                cases.Add(new ClosedCase<int>("B-A15", "BA", 615, 615));
                return cases.ToArray();
            }
        }

        public static class BoundaryProviderB
        {
            public static CatalogCase[] CreateCases()
            {
                var cases = new List<CatalogCase>();
                cases.Add(new ClosedCase<string>(
                    "B-B00",
                    "BB",
                    700,
                    "boundary",
                    CreateText,
                    MeasureText));
                return cases.ToArray();
            }

            private static string CreateText() => "created";

            private static int MeasureText(string value) => value.Length;
        }

        public static class BoundaryCatalog
        {
            private static readonly CatalogCase[] CachedEntries = CreateEntries();

            public static IReadOnlyList<CatalogCase> Entries => CachedEntries;

            private static CatalogCase[] CreateEntries()
            {
                var entries = new List<CatalogCase>();
                entries.AddRange(BoundaryProviderA.CreateCases());
                entries.AddRange(BoundaryProviderB.CreateCases());
                return entries.ToArray();
            }
        }

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                if (input == 1)
                {
                    return VerifyGenericCatalog();
                }

                return input switch
                {
                    0 => VerifyCatalog(Catalog.Entries, 914, 1, 101, 108),
                    2 => VerifyCatalog(
                        DirectArrayCatalog.Entries,
                        914,
                        1,
                        101,
                        108),
                    3 => VerifyCatalog(CopyCatalog.Entries, 914, 1, 101, 108),
                    4 => VerifyCatalog(ListCatalog.Entries, 914, 1, 101, 108),
                    5 => VerifyCachedCatalog(),
                    6 => VerifyCapacityCatalog(),
                    7 => VerifyBoundaryCatalog(),
                    _ => -1,
                };
            }

            private static int VerifyCatalog(
                IReadOnlyList<Entry> entries,
                int expectedChecksum,
                int expectedFirstValue,
                int expectedSecondStart,
                int expectedLastValue)
            {
                var count = 0;
                var checksum = 0;
                foreach (var entry in entries)
                {
                    count++;
                    checksum += entry.Value;
                }

                return entries.Count == 20 &&
                    count == 20 &&
                    checksum == expectedChecksum &&
                    entries[0].Value == expectedFirstValue &&
                    entries[0].Provider == "A" &&
                    entries[12].Value == expectedSecondStart &&
                    entries[12].Provider == "B" &&
                    entries[19].Value == expectedLastValue
                    ? checksum
                    : -1;
            }

            private static int VerifyGenericCatalog()
            {
                var result = VerifyCatalog(
                    GenericCatalog.Entries,
                    4914,
                    201,
                    301,
                    308);

                return result == 4914 &&
                    ReferenceEquals(
                        GenericCatalog.Entries[0].Marker,
                        GenericProvider<MarkerA>.Marker) &&
                    ReferenceEquals(
                        GenericCatalog.Entries[12].Marker,
                        GenericProvider<MarkerB>.Marker) &&
                    !ReferenceEquals(
                        GenericProvider<MarkerA>.Marker,
                        GenericProvider<MarkerB>.Marker)
                    ? result
                    : -1;
            }

            private static int VerifyCachedCatalog()
            {
                var entries = CachedCatalog.Entries;
                var count = 0;
                var checksum = 0;
                foreach (var entry in entries)
                {
                    count++;
                    checksum += entry.Content;
                }

                if (entries.Count != 20 || count != 20 || checksum != 3278 ||
                    !IsEntry(entries[0], "A00", "A", 100) ||
                    !IsEntry(entries[1], "A01", "A", 101) ||
                    !IsEntry(entries[2], "A02", "A", 102) ||
                    !IsEntry(entries[3], "A03", "A", 103) ||
                    !IsEntry(entries[4], "A04", "A", 104) ||
                    !IsEntry(entries[5], "A05", "A", 105) ||
                    !IsEntry(entries[6], "A06", "A", 106) ||
                    !IsEntry(entries[7], "A07", "A", 107) ||
                    !IsEntry(entries[8], "A08", "A", 108) ||
                    !IsEntry(entries[9], "A09", "A", 109) ||
                    !IsEntry(entries[10], "A10", "A", 110) ||
                    !IsEntry(entries[11], "A11", "A", 111) ||
                    !IsEntry(entries[12], "B00", "B", 200) ||
                    !IsEntry(entries[13], "B01", "B", 201) ||
                    !IsEntry(entries[14], "B02", "B", 202) ||
                    !IsEntry(entries[15], "B03", "B", 203) ||
                    !IsEntry(entries[16], "C00", "C", 300) ||
                    !IsEntry(entries[17], "C01", "C", 301) ||
                    !IsEntry(entries[18], "C02", "C", 302) ||
                    !IsEntry(entries[19], "C03", "C", 303) ||
                    entries[19] is not ClosedCase<string> typed ||
                    typed.Creator is null ||
                    typed.Invoker is null)
                {
                    return -1;
                }

                return checksum;
            }

            private static int VerifyCapacityCatalog()
            {
                var entries = CapacityCatalog.Entries;
                var count = 0;
                var checksum = 0;
                foreach (var entry in entries)
                {
                    count++;
                    checksum += entry.Content;
                }

                if (entries.Count != 20 || count != 20 || checksum != 8894 ||
                    !IsEntry(entries[0], "G-A00", "GA", 400) ||
                    !IsEntry(entries[1], "G-A01", "GA", 401) ||
                    !IsEntry(entries[2], "G-A02", "GA", 402) ||
                    !IsEntry(entries[3], "G-A03", "GA", 403) ||
                    !IsEntry(entries[4], "G-A04", "GA", 404) ||
                    !IsEntry(entries[5], "G-A05", "GA", 405) ||
                    !IsEntry(entries[6], "G-A06", "GA", 406) ||
                    !IsEntry(entries[7], "G-A07", "GA", 407) ||
                    !IsEntry(entries[8], "G-A08", "GA", 408) ||
                    !IsEntry(entries[9], "G-A09", "GA", 409) ||
                    !IsEntry(entries[10], "G-A10", "GA", 410) ||
                    !IsEntry(entries[11], "G-A11", "GA", 411) ||
                    !IsEntry(entries[12], "G-B00", "GB", 500) ||
                    !IsEntry(entries[13], "G-B01", "GB", 501) ||
                    !IsEntry(entries[14], "G-B02", "GB", 502) ||
                    !IsEntry(entries[15], "G-B03", "GB", 503) ||
                    !IsEntry(entries[16], "G-B04", "GB", 504) ||
                    !IsEntry(entries[17], "G-B05", "GB", 505) ||
                    !IsEntry(entries[18], "G-B06", "GB", 506) ||
                    !IsEntry(entries[19], "G-B07", "GB", 507) ||
                    entries[19] is not ClosedCase<string> typed ||
                    typed.Creator is null ||
                    typed.Invoker is null)
                {
                    return -1;
                }

                return checksum;
            }

            private static int VerifyBoundaryCatalog()
            {
                var entries = BoundaryCatalog.Entries;
                var count = 0;
                var checksum = 0;
                foreach (var entry in entries)
                {
                    count++;
                    checksum += entry.Content;
                }

                if (entries.Count != 17 || count != 17 || checksum != 10420 ||
                    !IsEntry(entries[0], "B-A00", "BA", 600) ||
                    !IsEntry(entries[1], "B-A01", "BA", 601) ||
                    !IsEntry(entries[2], "B-A02", "BA", 602) ||
                    !IsEntry(entries[3], "B-A03", "BA", 603) ||
                    !IsEntry(entries[4], "B-A04", "BA", 604) ||
                    !IsEntry(entries[5], "B-A05", "BA", 605) ||
                    !IsEntry(entries[6], "B-A06", "BA", 606) ||
                    !IsEntry(entries[7], "B-A07", "BA", 607) ||
                    !IsEntry(entries[8], "B-A08", "BA", 608) ||
                    !IsEntry(entries[9], "B-A09", "BA", 609) ||
                    !IsEntry(entries[10], "B-A10", "BA", 610) ||
                    !IsEntry(entries[11], "B-A11", "BA", 611) ||
                    !IsEntry(entries[12], "B-A12", "BA", 612) ||
                    !IsEntry(entries[13], "B-A13", "BA", 613) ||
                    !IsEntry(entries[14], "B-A14", "BA", 614) ||
                    !IsEntry(entries[15], "B-A15", "BA", 615) ||
                    !IsEntry(entries[16], "B-B00", "BB", 700) ||
                    entries[16] is not ClosedCase<string> typed ||
                    typed.Creator is null ||
                    typed.Invoker is null)
                {
                    return -1;
                }

                return checksum;
            }

            private static bool IsEntry(
                CatalogCase entry,
                string identity,
                string provider,
                int content) =>
                entry.Identity == identity &&
                entry.Provider == provider &&
                entry.Content == content;

            public static int Trace() => 0;
        }
        """;
}
