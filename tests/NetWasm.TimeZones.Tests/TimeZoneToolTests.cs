using System.Collections.Immutable;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NetWasm.TimeZones;

namespace NetWasm.TimeZones.Tests;

public sealed class TimeZoneToolTests
{
    [Fact]
    public void PosixRulesPreserveNorthernAndSouthernFutureTransitions()
    {
        var expander = As<IPosixFutureRuleExpander>(new PosixFutureRuleExpander(
            new PosixRuleParser()));
        var after = new DateTimeOffset(2025, 12, 31, 0, 0, 0, TimeSpan.Zero)
            .ToUnixTimeSeconds();

        var transitions = expander.Expand(
            "AEST-10AEDT,M10.1.0,M4.1.0/3",
            after);

        Assert.Equal(
            new DateTimeOffset(2026, 4, 4, 16, 0, 0, TimeSpan.Zero)
                .ToUnixTimeSeconds(),
            transitions[0].UnixSeconds);
        Assert.Equal(36_000, transitions[0].OffsetSeconds);
        Assert.False(transitions[0].IsDaylightSavingTime);
        Assert.Equal(
            new DateTimeOffset(2026, 10, 3, 16, 0, 0, TimeSpan.Zero)
                .ToUnixTimeSeconds(),
            transitions[1].UnixSeconds);
        Assert.Equal(39_600, transitions[1].OffsetSeconds);
        Assert.True(transitions[1].IsDaylightSavingTime);
        Assert.True(transitions[^1].UnixSeconds <= 253_402_300_799);
    }

    [Fact]
    public void PosixParserSupportsEveryDateAndTimeForm()
    {
        var parser = As<IPosixRuleParser>(new PosixRuleParser());

        Assert.Null(parser.Parse("UTC0"));
        var julian = Assert.IsType<PosixFutureRule>(parser.Parse(
            "<+01>-1<+02>,J60/1:02:03s,300/-2u"));
        Assert.Equal(3_600, julian.StandardOffsetSeconds);
        Assert.Equal(7_200, julian.DaylightOffsetSeconds);
        Assert.Equal(PosixDateRuleKind.JulianWithoutLeapDay, julian.Start.Date.Kind);
        Assert.Equal(PosixTimeBasis.Standard, julian.Start.Basis);
        Assert.Equal(PosixDateRuleKind.ZeroBasedDayOfYear, julian.End.Date.Kind);
        Assert.Equal(PosixTimeBasis.Utc, julian.End.Basis);
        Assert.Equal(-7_200, julian.End.Seconds);

        Assert.Throws<ArgumentException>(() => parser.Parse(""));
        Assert.Throws<FormatException>(() => parser.Parse("AB0"));
        Assert.Throws<FormatException>(() => parser.Parse("<>0"));
        Assert.Throws<FormatException>(() => parser.Parse("STD0DST"));
        Assert.Throws<FormatException>(() => parser.Parse("STD0DST,M13.1.0,M1.1.0"));
        Assert.Throws<FormatException>(() => parser.Parse("STD0DST,M1.0.0,M1.1.0"));
        Assert.Throws<FormatException>(() => parser.Parse("STD0DST,M1.1.7,M1.1.0"));
        Assert.Throws<FormatException>(() => parser.Parse("STD0DST,J0,J1"));
        Assert.Throws<FormatException>(() => parser.Parse("STD0DST,366,1"));
        Assert.Throws<FormatException>(() => parser.Parse("STD0DST,M1.1.0/168,M2.1.0"));
        Assert.Throws<FormatException>(() => parser.Parse("STD0DST,M1.1.0/1:60,M2.1.0"));
        Assert.Throws<FormatException>(() => parser.Parse("STD0DST,M1.1.0/1:00:60,M2.1.0"));
        Assert.Throws<FormatException>(() => parser.Parse("STD0DST,M1.1.0,M2.1.0x"));
        Assert.Throws<FormatException>(() => parser.Parse("STD+DST,M1.1.0,M2.1.0"));
        Assert.Throws<FormatException>(() => parser.Parse("STD0DST,M.1.0,M2.1.0"));
        Assert.Throws<FormatException>(() => parser.Parse("STD0DST,M1.1.0/,M2.1.0"));
        Assert.Throws<FormatException>(() => parser.Parse("STD999999999999DST,M1.1.0,M2.1.0"));
        Assert.NotNull(parser.Parse("STD+0DST+1,M1.5.0/2w,M2.1.0/2g"));
        Assert.NotNull(parser.Parse("abc0def,M1.1.0/2z,M2.1.0/2u"));
        Assert.Throws<FormatException>(() => parser.Parse("<abc0"));
        Assert.Throws<FormatException>(() => parser.Parse("STD0DST,M1.1.0/,M2.1.0"));
        Assert.Throws<FormatException>(() => parser.Parse("STD0DST,M1.1.0/1:,M2.1.0"));
        Assert.Throws<FormatException>(() => parser.Parse("STD0DST,,M2.1.0"));
        Assert.Throws<FormatException>(() => parser.Parse("STD"));
        Assert.Throws<FormatException>(() => parser.Parse("STD0DST,"));
        Assert.NotNull(parser.Parse("STD0DST,M1.1.0/1:02,M2.1.0/1:02"));
    }

    [Fact]
    public void PosixExpansionCoversLeapDayLastWeekAndDateTimeBounds()
    {
        var expander = As<IPosixFutureRuleExpander>(new PosixFutureRuleExpander(
            new PosixRuleParser()));
        var leapBoundary = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)
            .ToUnixTimeSeconds();

        var julian = expander.Expand("STD0DST,J60,J61", leapBoundary);
        Assert.Contains(julian, transition =>
            transition.UnixSeconds == new DateTimeOffset(
                2024, 3, 1, 2, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds());

        var lastWeek = expander.Expand("STD0DST,M2.5.0,365", leapBoundary);
        Assert.NotEmpty(lastWeek);
        Assert.Empty(expander.Expand("UTC0", long.MinValue));
        Assert.Empty(expander.Expand(
            "STD0DST,365/167,365/167",
            DateTimeOffset.MaxValue.ToUnixTimeSeconds() - 1));
        Assert.NotEmpty(expander.Expand(
            "STD0DST,M1.1.0/2s,M2.1.0/2u",
            leapBoundary));
    }

    [Fact]
    public void TzifParserRetainsHistoryAndMaterializesFutureRules()
    {
        var parser = As<ITzifParser>(new TzifParser(
            new PosixFutureRuleExpander(new PosixRuleParser())));

        var zone = parser.Parse(
            "Test/Zone",
            BuildTzif("STD0DST,M3.2.0,M11.1.0"));

        Assert.Equal("Test/Zone", zone.Name);
        Assert.Equal(0, zone.InitialOffsetSeconds);
        Assert.False(zone.InitialDaylightSavingTime);
        Assert.Equal(0, zone.Transitions[0].UnixSeconds);
        Assert.Equal(3_600, zone.Transitions[0].OffsetSeconds);
        Assert.True(zone.Transitions[0].IsDaylightSavingTime);
        Assert.True(zone.Transitions.Length > 10_000);
        Assert.Throws<ArgumentException>(() => parser.Parse("", []));
        Assert.Throws<ArgumentNullException>(() => parser.Parse("Zone", null!));
        Assert.Throws<FormatException>(() => parser.Parse("Zone", []));
    }

    [Fact]
    public void TzifParserRejectsEveryMalformedStructuralBoundary()
    {
        var parser = As<ITzifParser>(new TzifParser(
            new PosixFutureRuleExpander(new PosixRuleParser())));

        Assert.Equal(0, parser.Parse("V1", BuildTzifV1()).InitialOffsetSeconds);
        Assert.Single(parser.Parse("V1", BuildTzifV1([0])).Transitions);
        Assert.True(parser.Parse("Daylight", BuildTzifV1([], daylight: true))
            .InitialDaylightSavingTime);
        Assert.Single(parser.Parse("Future", BuildTzifV1([int.MaxValue])).Transitions);
        Assert.Equal(3_600, parser.Parse(
            "Ancient",
            BuildTzifWithTransitions(
                [-63_000_000_000],
                typeIndex: 1,
                secondOffset: 3_600))
            .InitialOffsetSeconds);
        Assert.Empty(parser.Parse(
            "Beyond",
            BuildTzifWithTransitions([254_000_000_000])).Transitions);
        Assert.NotEmpty(parser.Parse(
            "FooterOnly",
            BuildTzifWithTransitions([], footer: "STD0DST,M1.1.0,M2.1.0"))
            .Transitions);
        Assert.Throws<FormatException>(() => parser.Parse("Zone", [.. Encoding.ASCII.GetBytes("NOPE"), .. new byte[40]]));

        var unsupported = BuildTzif("UTC0");
        unsupported[4] = (byte)'5';
        Assert.Throws<FormatException>(() => parser.Parse("Zone", unsupported));

        var noTypes = BuildTzif("UTC0");
        WriteBigEndian(noTypes, 36, 0);
        Assert.Throws<FormatException>(() => parser.Parse("Zone", noTypes));

        var tooManyTypes = BuildTzif("UTC0");
        WriteBigEndian(tooManyTypes, 36, 257);
        Assert.Throws<FormatException>(() => parser.Parse("Zone", tooManyTypes));

        var negativeCount = BuildTzif("UTC0");
        WriteBigEndian(negativeCount, 32, -1);
        Assert.Throws<FormatException>(() => parser.Parse("Zone", negativeCount));

        var invalidType = BuildTzif("UTC0");
        invalidType[114] = 9;
        Assert.Throws<FormatException>(() => parser.Parse("Zone", invalidType));

        var unordered = BuildTzifWithTransitions([1, 1]);
        Assert.Throws<FormatException>(() => parser.Parse("Zone", unordered));

        var missingFooterStart = BuildTzif("UTC0");
        missingFooterStart[^6] = (byte)'x';
        Assert.Throws<FormatException>(() => parser.Parse("Zone", missingFooterStart));

        var missingFooterEnd = BuildTzif("UTC0")[..^1];
        Assert.Throws<FormatException>(() => parser.Parse("Zone", missingFooterEnd));

        byte[] trailingFooter = [.. BuildTzif("UTC0"), (byte)'x'];
        Assert.Throws<FormatException>(() => parser.Parse("Zone", trailingFooter));

        Assert.Throws<FormatException>(() => parser.Parse("Zone", new byte[43]));
    }

    [Fact]
    public void SelectionIncludesCanonicalZonesAndEveryRequiredAlias()
    {
        var canonical = Zone("Area/Canonical", 3_600);
        var second = Zone("Area/Second", 7_200);
        var catalog = new TimeZoneCatalog(
            "2026c",
            ImmutableDictionary<string, TimeZoneDefinition>.Empty
                .Add(canonical.Name, canonical)
                .Add(second.Name, second),
            ImmutableDictionary<string, string>.Empty
                .Add("Area/Alias", canonical.Name));
        var selector = As<ITimeZoneSelectionSelector>(new TimeZoneSelectionSelector());

        Assert.Equal(
            ["Area/Alias", "Area/Canonical"],
            selector.Select(catalog, ["Area/Alias"], false)
                .Select(zone => zone.Name));
        Assert.Equal(
            ["Area/Alias", "Area/Canonical", "Area/Second"],
            selector.Select(catalog, [], true).Select(zone => zone.Name));
        Assert.Throws<ArgumentException>(() => selector.Select(catalog, [], false));
        Assert.Throws<ArgumentException>(() => selector.Select(catalog, ["Area/Second"], true));
        Assert.Throws<ArgumentException>(() => selector.Select(catalog, ["Missing"], false));
        Assert.Throws<ArgumentNullException>(() => selector.Select(null!, [], true));
        Assert.Throws<ArgumentNullException>(() => selector.Select(catalog, null!, true));
    }

    [Fact]
    public void AssetManifestAndBrotliEncodersAreDeterministic()
    {
        var assets = As<ITimeZoneAssetEncoder>(new TimeZoneAssetEncoder());
        var zones = ImmutableArray.Create(
            Zone("Area/A", 1),
            Zone("Area/B", 2));

        var first = assets.Encode("2026c", zones);
        var second = assets.Encode("2026c", zones);

        Assert.Equal(first.Contents, second.Contents);
        Assert.Equal(first.Identity, second.Identity);
        Assert.Equal(
            first.Identity,
            Convert.ToHexStringLower(SHA256.HashData(first.Contents[..^32])));
        Assert.Equal(first.Contents[^32..], SHA256.HashData(first.Contents[..^32]));

        var compressor = As<IBrotliAssetCompressor>(new BrotliAssetCompressor());
        var compressed = compressor.Compress(first.Contents);
        using var input = new MemoryStream(compressed);
        using var stream = new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        stream.CopyTo(output);
        Assert.Equal(first.Contents, output.ToArray());

        var manifest = new TimeZoneDeploymentManifest(
            1,
            "2026c",
            first.Identity,
            "zones.nwtz",
            first.Contents.Length,
            "zones.nwtz.br",
            compressed.Length,
            "zones.loader.mjs",
            ["Area/A", "Area/B"]);
        var manifests = As<ITimeZoneManifestEncoder>(new TimeZoneManifestEncoder());
        var manifestBytes = manifests.Encode(manifest);
        var manifestText = Encoding.UTF8.GetString(manifestBytes);
        Assert.Contains('\n', manifestText);
        Assert.DoesNotContain('\r', manifestText);
        using var document = JsonDocument.Parse(manifestBytes);
        Assert.Equal("2026c", document.RootElement.GetProperty("dataVersion").GetString());
        Assert.Throws<ArgumentNullException>(() => compressor.Compress(null!));
        Assert.Throws<ArgumentNullException>(() => manifests.Encode(null!));
        Assert.Throws<ArgumentException>(() => assets.Encode("", zones));
        Assert.Throws<ArgumentException>(() => assets.Encode("2026c", []));
        Assert.Throws<ArgumentException>(() => assets.Encode("2026c", [Zone("B", 0), Zone("A", 0)]));
        Assert.Throws<ArgumentException>(() => assets.Encode("2026c", [
            Zone("A", 0) with
            {
                Transitions = [new(2, 0, false), new(1, 0, false)],
            },
        ]));
        Assert.Throws<ArgumentException>(() => assets.Encode("2026c", [Zone("bad\nname", 0)]));
        Assert.Throws<ArgumentException>(() => assets.Encode("2026c", [Zone(new string('x', 65_536), 0)]));
    }

    [Fact]
    public void CommandLineParserRequiresExplicitPackagingChoices()
    {
        var parser = As<ITimeZoneCommandLineParser>(new TimeZoneCommandLineParser());
        var request = parser.Parse([
            "generate",
            "--source", "source",
            "--asset", "asset",
            "--brotli", "brotli",
            "--manifest", "manifest",
            "--browser-loader", "loader",
            "--zone", "Area/A",
            "--zone", "Area/B",
        ]);

        Assert.Equal(["Area/A", "Area/B"], request.Zones.ToArray());
        Assert.False(request.IncludeAll);
        Assert.True(parser.Parse([
            "generate", "--source", "s", "--asset", "a", "--brotli", "b",
            "--manifest", "m", "--browser-loader", "l", "--all",
        ]).IncludeAll);
        Assert.Throws<ArgumentNullException>(() => parser.Parse(null!));
        Assert.Throws<ArgumentException>(() => parser.Parse([]));
        Assert.Throws<ArgumentException>(() => parser.Parse(["other"]));
        Assert.Throws<ArgumentException>(() => parser.Parse(["generate", "--unknown"]));
        Assert.Throws<ArgumentException>(() => parser.Parse(["generate", "--source"]));
        Assert.Throws<ArgumentException>(() => parser.Parse([
            "generate", "--source", "s", "--asset", "a", "--brotli", "b",
            "--manifest", "m", "--browser-loader", "",
        ]));
        Assert.Throws<ArgumentException>(() => parser.Parse([
            "generate", "--source", "s", "--asset", "a", "--brotli", "b",
        ]));
    }

    [Fact]
    public void GenerationCommandWritesPayloadTransferAndManifestArtifacts()
    {
        var catalog = new TimeZoneCatalog(
            "2026c",
            ImmutableDictionary<string, TimeZoneDefinition>.Empty.Add(
                "Area/A",
                Zone("Area/A", 0)),
            ImmutableDictionary<string, string>.Empty);
        var writer = new RecordingArtifactWriter();
        var command = As<ITimeZoneGenerationCommand>(new TimeZoneGenerationCommand(
            new FixedCatalogReader(catalog),
            new TimeZoneSelectionSelector(),
            new TimeZoneAssetEncoder(),
            new BrotliAssetCompressor(),
            new TimeZoneManifestEncoder(),
            new TimeZoneBrowserLoaderEncoder(),
            writer));

        var manifest = command.Execute(new(
            "source",
            "output/zones.nwtz",
            "output/zones.nwtz.br",
            "output/zones.json",
            "output/zones.loader.mjs",
            ["Area/A"],
            false));

        Assert.Equal(4, writer.Artifacts.Count);
        Assert.Equal("zones.nwtz", manifest.AssetFile);
        Assert.Equal("zones.nwtz.br", manifest.BrotliFile);
        Assert.Equal("zones.loader.mjs", manifest.BrowserLoaderFile);
        var loader = Encoding.UTF8.GetString(
            writer.Artifacts["output/zones.loader.mjs"]);
        Assert.Contains("filesystem._setPreopens", loader);
        Assert.Contains("timeZone === \"UTC\"", loader);
        Assert.Equal(["Area/A"], manifest.Zones);
        Assert.Throws<ArgumentNullException>(() => command.Execute(null!));
    }

    [Fact]
    public void ToolApplicationReportsSuccessAndLoudFailure()
    {
        var manifest = new TimeZoneDeploymentManifest(
            1, "2026c", "identity", "a", 10, "b", 5, "loader", ["Area/A"]);
        var success = As<ITimeZoneToolApplication>(new TimeZoneToolApplication(
            new FixedCommandLineParser(),
            new FixedGenerationCommand(manifest)));
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.Equal(0, success.Run([], output, error));
        Assert.Contains("version=2026c zones=1", output.ToString());
        Assert.Equal(string.Empty, error.ToString());

        var failure = As<ITimeZoneToolApplication>(new TimeZoneToolApplication(
            new FailingCommandLineParser(),
            new FixedGenerationCommand(manifest)));
        Assert.Equal(1, failure.Run([], output, error));
        Assert.Contains("invalid options", error.ToString());
        Assert.Throws<ArgumentNullException>(() => success.Run(null!, output, error));
        Assert.Throws<ArgumentNullException>(() => success.Run([], null!, error));
        Assert.Throws<ArgumentNullException>(() => success.Run([], output, null!));
    }

    [Fact]
    public void VersionAndArtifactAdaptersUseExplicitFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var versions = As<IIanaVersionReader>(new IanaVersionReader());
            File.WriteAllText(Path.Combine(directory, "version"), "2026c\n");
            Assert.Equal("2026c", versions.Read(directory));
            File.Delete(Path.Combine(directory, "version"));
            File.WriteAllText(Path.Combine(directory, "+VERSION"), "2026d\n");
            Assert.Equal("2026d", versions.Read(directory));
            File.Delete(Path.Combine(directory, "+VERSION"));
            File.WriteAllText(Path.Combine(directory, "tzdata.zi"), "# version 2026e\n");
            Assert.Equal("2026e", versions.Read(directory));

            var writer = As<IArtifactWriter>(new ArtifactWriter());
            var artifact = Path.Combine(directory, "nested", "asset.bin");
            writer.Write(artifact, [1, 2, 3]);
            Assert.Equal([1, 2, 3], File.ReadAllBytes(artifact));
            Assert.Throws<ArgumentException>(() => writer.Write("", []));
            Assert.Throws<ArgumentNullException>(() => writer.Write(artifact, null!));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CatalogReaderIndexesTzifFilesAliasesAndIgnoresMetadata()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var area = Path.Combine(directory, "Area");
        Directory.CreateDirectory(area);
        try
        {
            File.WriteAllText(Path.Combine(directory, "version"), "2026c\n");
            var canonical = Path.Combine(area, "Canonical");
            File.WriteAllBytes(canonical, BuildTzif("UTC0"));
            File.WriteAllText(Path.Combine(directory, "zone.tab"), "metadata");
            Directory.CreateDirectory(Path.Combine(directory, "posix"));
            File.WriteAllBytes(Path.Combine(directory, "posix", "Ignored"), BuildTzif("UTC0"));
            Directory.CreateDirectory(Path.Combine(directory, "right"));
            File.WriteAllBytes(Path.Combine(directory, "right", "Ignored"), BuildTzif("UTC0"));
            foreach (var metadata in new[]
            {
                "localtime", "posixrules", "zone1970.tab", "iso3166.tab",
                "leapseconds", "leap-seconds.list",
            })
            {
                File.WriteAllText(Path.Combine(directory, metadata), "metadata");
            }
            File.WriteAllText(Path.Combine(directory, "not-a-zone"), "ordinary data");
            File.CreateSymbolicLink(
                Path.Combine(area, "Alias"),
                "Canonical");
            var reader = As<ITimeZoneCatalogReader>(new TimeZoneCatalogReader(
                new IanaVersionReader(),
                new TzifParser(new PosixFutureRuleExpander(new PosixRuleParser()))));

            var catalog = reader.Read(directory);

            Assert.Equal("2026c", catalog.DataVersion);
            Assert.Equal(["Area/Canonical"], catalog.Definitions.Keys);
            Assert.Equal("Area/Canonical", catalog.Aliases["Area/Alias"]);
            Assert.Throws<ArgumentException>(() => reader.Read(""));
            Assert.Throws<DirectoryNotFoundException>(() => reader.Read(
                Path.Combine(directory, "missing")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CatalogReaderRejectsEmptyMissingAndEscapingAliasCatalogs()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "version"), "2026c\n");
            var reader = As<ITimeZoneCatalogReader>(new TimeZoneCatalogReader(
                new IanaVersionReader(),
                new FakeTzifParser()));
            Assert.Throws<InvalidOperationException>(() => reader.Read(directory));

            var realZone = Path.Combine(directory, "RealZone");
            File.WriteAllBytes(realZone, BuildTzif("UTC0"));
            File.CreateSymbolicLink(Path.Combine(directory, "MissingAlias"), "Missing");
            var missing = Assert.Throws<InvalidOperationException>(() => reader.Read(directory));
            Assert.Contains("targets missing zone", missing.Message);
            File.Delete(Path.Combine(directory, "MissingAlias"));
            File.Delete(realZone);

            var outside = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            File.WriteAllBytes(outside, BuildTzif("UTC0"));
            try
            {
                File.CreateSymbolicLink(Path.Combine(directory, "EscapingAlias"), outside);
                Assert.Throws<InvalidOperationException>(() => reader.Read(directory));
            }
            finally
            {
                File.Delete(outside);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void VersionReaderRejectsMissingAndMalformedIdentity()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var reader = As<IIanaVersionReader>(new IanaVersionReader());
            Assert.Throws<ArgumentException>(() => reader.Read(""));
            Assert.Throws<InvalidOperationException>(() => reader.Read(directory));
            File.WriteAllText(Path.Combine(directory, "tzdata.zi"), "invalid\n");
            Assert.Throws<InvalidOperationException>(() => reader.Read(directory));
            File.WriteAllText(Path.Combine(directory, "tzdata.zi"), string.Empty);
            Assert.Throws<InvalidOperationException>(() => reader.Read(directory));
            File.WriteAllText(Path.Combine(directory, "version"), "bad");
            Assert.Throws<InvalidOperationException>(() => reader.Read(directory));
            File.WriteAllText(Path.Combine(directory, "version"), "2026C");
            Assert.Throws<InvalidOperationException>(() => reader.Read(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CompositionRootAndConstructorsValidateEveryDependency()
    {
        Assert.IsAssignableFrom<ITimeZoneToolApplication>(Program.Create());
        Assert.Equal(1, Program.Main(["invalid"]));
        Assert.Throws<ArgumentNullException>(() =>
            new PosixFutureRuleExpander(null!));
        Assert.Throws<ArgumentNullException>(() =>
            new TzifParser(null!));
        Assert.Throws<ArgumentNullException>(() =>
            new TimeZoneCatalogReader(null!, new FakeTzifParser()));
        Assert.Throws<ArgumentNullException>(() =>
            new TimeZoneCatalogReader(new IanaVersionReader(), null!));
        Assert.Throws<ArgumentNullException>(() =>
            new TimeZoneToolApplication(null!, new FixedGenerationCommand(
                Manifest())));
        Assert.Throws<ArgumentNullException>(() =>
            new TimeZoneToolApplication(new FixedCommandLineParser(), null!));

        var catalog = new FixedCatalogReader(new TimeZoneCatalog(
            "2026c",
            ImmutableDictionary<string, TimeZoneDefinition>.Empty,
            ImmutableDictionary<string, string>.Empty));
        var selector = new TimeZoneSelectionSelector();
        var assets = new TimeZoneAssetEncoder();
        var brotli = new BrotliAssetCompressor();
        var manifests = new TimeZoneManifestEncoder();
        var loaders = new TimeZoneBrowserLoaderEncoder();
        var writer = new RecordingArtifactWriter();
        Assert.Throws<ArgumentNullException>(() => new TimeZoneGenerationCommand(
            null!, selector, assets, brotli, manifests, loaders, writer));
        Assert.Throws<ArgumentNullException>(() => new TimeZoneGenerationCommand(
            catalog, null!, assets, brotli, manifests, loaders, writer));
        Assert.Throws<ArgumentNullException>(() => new TimeZoneGenerationCommand(
            catalog, selector, null!, brotli, manifests, loaders, writer));
        Assert.Throws<ArgumentNullException>(() => new TimeZoneGenerationCommand(
            catalog, selector, assets, null!, manifests, loaders, writer));
        Assert.Throws<ArgumentNullException>(() => new TimeZoneGenerationCommand(
            catalog, selector, assets, brotli, null!, loaders, writer));
        Assert.Throws<ArgumentNullException>(() => new TimeZoneGenerationCommand(
            catalog, selector, assets, brotli, manifests, null!, writer));
        Assert.Throws<ArgumentNullException>(() => new TimeZoneGenerationCommand(
            catalog, selector, assets, brotli, manifests, loaders, null!));
    }

    [Fact]
    public void BrowserLoaderSkipsUtcAndMountsOnlySuccessfulAssetResponses()
    {
        var encoder = As<ITimeZoneBrowserLoaderEncoder>(
            new TimeZoneBrowserLoaderEncoder());
        var source = Encoding.UTF8.GetString(encoder.Encode("zones.nwtz"));

        Assert.Contains("timeZone == null", source);
        Assert.Contains("response.ok", source);
        Assert.Contains("/netwasm-timezones", source);
        Assert.Contains("zones.nwtz", source);
        Assert.Throws<ArgumentException>(() => encoder.Encode(""));
    }

    private static TimeZoneDeploymentManifest Manifest() => new(
        1,
        "2026c",
        "identity",
        "asset",
        1,
        "brotli",
        1,
        "loader",
        []);

    private static TimeZoneDefinition Zone(string name, int offset) =>
        new(name, offset, false, []);

    private static T As<T>(object value) where T : class =>
        Assert.IsAssignableFrom<T>(value);

    private static byte[] BuildTzif(string footer)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        WriteHeader(writer, '2');
        WriteInt32(writer, 0);
        writer.Write((byte)1);
        WriteType(writer, 0, false);
        WriteType(writer, 3_600, true);
        writer.Write((byte)0);
        WriteHeader(writer, '2');
        WriteInt64(writer, 0);
        writer.Write((byte)1);
        WriteType(writer, 0, false);
        WriteType(writer, 3_600, true);
        writer.Write((byte)0);
        writer.Write((byte)'\n');
        writer.Write(Encoding.ASCII.GetBytes(footer));
        writer.Write((byte)'\n');
        return stream.ToArray();

        static void WriteHeader(BinaryWriter writer, char version)
        {
            writer.Write(Encoding.ASCII.GetBytes("TZif"));
            writer.Write((byte)version);
            writer.Write(new byte[15]);
            WriteInt32(writer, 0);
            WriteInt32(writer, 0);
            WriteInt32(writer, 0);
            WriteInt32(writer, 1);
            WriteInt32(writer, 2);
            WriteInt32(writer, 1);
        }

        static void WriteType(BinaryWriter writer, int offset, bool daylight)
        {
            WriteInt32(writer, offset);
            writer.Write(daylight ? (byte)1 : (byte)0);
            writer.Write((byte)0);
        }

        static void WriteInt32(BinaryWriter writer, int value)
        {
            writer.Write((byte)(value >> 24));
            writer.Write((byte)(value >> 16));
            writer.Write((byte)(value >> 8));
            writer.Write((byte)value);
        }

        static void WriteInt64(BinaryWriter writer, long value)
        {
            WriteInt32(writer, (int)(value >> 32));
            WriteInt32(writer, (int)value);
        }
    }

    private static byte[] BuildTzifV1(
        long[]? transitions = null,
        bool daylight = false,
        int secondOffset = 0)
    {
        transitions ??= [];
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        WriteTzifHeader(writer, '\0', transitions.Length, secondOffset == 0 ? 1 : 2);
        foreach (var transition in transitions)
        {
            WriteBigEndian(writer, unchecked((int)transition));
        }
        writer.Write(Enumerable.Repeat(
            secondOffset == 0 ? (byte)0 : (byte)1,
            transitions.Length).ToArray());
        WriteBigEndian(writer, 0);
        writer.Write(daylight ? (byte)1 : (byte)0);
        writer.Write((byte)0);
        if (secondOffset != 0)
        {
            WriteBigEndian(writer, secondOffset);
            writer.Write((byte)0);
            writer.Write((byte)0);
        }
        writer.Write((byte)0);
        return stream.ToArray();
    }

    private static byte[] BuildTzifWithTransitions(
        long[] transitions,
        byte typeIndex = 0,
        int secondOffset = 0,
        string footer = "")
    {
        var typeCount = secondOffset == 0 ? 1 : 2;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        WriteTzifHeader(writer, '2', transitions.Length, typeCount);
        foreach (var transition in transitions)
        {
            WriteBigEndian(writer, unchecked((int)transition));
        }
        writer.Write(Enumerable.Repeat(typeIndex, transitions.Length).ToArray());
        WriteBigEndian(writer, 0);
        writer.Write((byte)0);
        writer.Write((byte)0);
        if (secondOffset != 0)
        {
            WriteBigEndian(writer, secondOffset);
            writer.Write((byte)0);
            writer.Write((byte)0);
        }
        writer.Write((byte)0);
        WriteTzifHeader(writer, '2', transitions.Length, typeCount);
        foreach (var transition in transitions)
        {
            WriteBigEndian(writer, transition);
        }
        writer.Write(Enumerable.Repeat(typeIndex, transitions.Length).ToArray());
        WriteBigEndian(writer, 0);
        writer.Write((byte)0);
        writer.Write((byte)0);
        if (secondOffset != 0)
        {
            WriteBigEndian(writer, secondOffset);
            writer.Write((byte)0);
            writer.Write((byte)0);
        }
        writer.Write((byte)0);
        writer.Write((byte)'\n');
        writer.Write(Encoding.ASCII.GetBytes(footer));
        writer.Write((byte)'\n');
        return stream.ToArray();
    }

    private static void WriteTzifHeader(
        BinaryWriter writer,
        char version,
        int transitionCount,
        int typeCount)
    {
        writer.Write(Encoding.ASCII.GetBytes("TZif"));
        writer.Write((byte)version);
        writer.Write(new byte[15]);
        WriteBigEndian(writer, 0);
        WriteBigEndian(writer, 0);
        WriteBigEndian(writer, 0);
        WriteBigEndian(writer, transitionCount);
        WriteBigEndian(writer, typeCount);
        WriteBigEndian(writer, 1);
    }

    private static void WriteBigEndian(byte[] contents, int offset, int value)
    {
        contents[offset] = (byte)(value >> 24);
        contents[offset + 1] = (byte)(value >> 16);
        contents[offset + 2] = (byte)(value >> 8);
        contents[offset + 3] = (byte)value;
    }

    private static void WriteBigEndian(BinaryWriter writer, int value)
    {
        writer.Write((byte)(value >> 24));
        writer.Write((byte)(value >> 16));
        writer.Write((byte)(value >> 8));
        writer.Write((byte)value);
    }

    private static void WriteBigEndian(BinaryWriter writer, long value)
    {
        WriteBigEndian(writer, (int)(value >> 32));
        WriteBigEndian(writer, (int)value);
    }

    private sealed class RecordingArtifactWriter : IArtifactWriter
    {
        internal Dictionary<string, byte[]> Artifacts { get; } = [];

        public void Write(string path, byte[] contents) => Artifacts.Add(path, contents);
    }

    private sealed class FixedCatalogReader(TimeZoneCatalog catalog) : ITimeZoneCatalogReader
    {
        public TimeZoneCatalog Read(string sourceDirectory) => catalog;
    }

    private sealed class FakeTzifParser : ITzifParser
    {
        public TimeZoneDefinition Parse(string name, byte[] contents) => Zone(name, 0);
    }

    private sealed class FixedCommandLineParser : ITimeZoneCommandLineParser
    {
        public TimeZoneGenerationRequest Parse(string[] arguments) => new(
            "source", "asset", "brotli", "manifest", "loader", ["Area/A"], false);
    }

    private sealed class FailingCommandLineParser : ITimeZoneCommandLineParser
    {
        public TimeZoneGenerationRequest Parse(string[] arguments) =>
            throw new ArgumentException("invalid options");
    }

    private sealed class FixedGenerationCommand(TimeZoneDeploymentManifest manifest) :
        ITimeZoneGenerationCommand
    {
        public TimeZoneDeploymentManifest Execute(TimeZoneGenerationRequest request) =>
            manifest;
    }
}
