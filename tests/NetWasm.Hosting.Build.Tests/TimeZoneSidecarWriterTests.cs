// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Hosting.Build.TimeZones;
using NetWasm.TimeZones;

namespace NetWasm.Hosting.Build.Tests;

public sealed class TimeZoneSidecarWriterTests
{
    [Fact]
    public void WritesSelectedZonesAndTheirAliasesFromThePinnedSource()
    {
        using var directory = TemporaryDirectory.Create();
        var output = Path.Combine(directory.Path, "obj", "App.wasm.tz-info");
        var writer = Assert.IsAssignableFrom<ITimeZoneSidecarWriter>(new TimeZoneSidecarWriter());

        var sidecar = writer.Write(new(VendoredSource(), TimeZoneSidecarMode.Selected,
            ["Australia/Melbourne", "Asia/Calcutta"], output));

        Assert.Equal("2026c", sidecar.DataVersion);
        Assert.Matches("^[0-9a-f]{64}$", sidecar.Identity);
        Assert.True(sidecar.ZoneCount >= 3, $"expected the alias and its target, got {sidecar.ZoneCount}");
        Assert.True(new FileInfo(output).Length > 0);
        var again = writer.Write(new(VendoredSource(), TimeZoneSidecarMode.Selected,
            ["Asia/Calcutta", "Australia/Melbourne"], Path.Combine(directory.Path, "again.tz-info")));
        Assert.Equal(sidecar.Identity, again.Identity);
        Assert.Equal(File.ReadAllBytes(output), File.ReadAllBytes(Path.Combine(directory.Path, "again.tz-info")));
    }

    [Fact]
    public void FullModeSelectsEveryZoneAndRejectsExplicitZones()
    {
        using var directory = TemporaryDirectory.Create();
        var selections = new RecordingSelector();
        var writer = new TimeZoneSidecarWriter(new FixedCatalogReader(), selections, new FixedEncoder());
        var output = Path.Combine(directory.Path, "Full.tz-info");

        var sidecar = writer.Write(new(directory.Path, TimeZoneSidecarMode.Full, [], output));

        Assert.True(selections.IncludeAll);
        Assert.Equal(new TimeZoneSidecar("2026c", "identity", 0), sidecar);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(output));
        Assert.Throws<ArgumentException>(() => writer.Write(new(directory.Path, TimeZoneSidecarMode.Full,
            ["Australia/Melbourne"], output)));
    }

    [Fact]
    public void RejectsUnknownZonesRelativePathsAndMissingCollaborators()
    {
        using var directory = TemporaryDirectory.Create();
        var writer = new TimeZoneSidecarWriter();
        Assert.Throws<ArgumentException>(() => writer.Write(new(VendoredSource(), TimeZoneSidecarMode.Selected,
            ["Mars/Olympus_Mons"], Path.Combine(directory.Path, "x.tz-info"))));
        Assert.Throws<ArgumentException>(() => writer.Write(new("zoneinfo", TimeZoneSidecarMode.Selected,
            ["UTC"], Path.Combine(directory.Path, "x.tz-info"))));
        Assert.Throws<ArgumentException>(() => writer.Write(new(VendoredSource(), TimeZoneSidecarMode.Selected,
            ["UTC"], "x.tz-info")));
        Assert.Throws<ArgumentNullException>(() => writer.Write(null!));
        Assert.Throws<ArgumentNullException>(() => writer.Write(new(VendoredSource(), TimeZoneSidecarMode.Selected,
            null!, Path.Combine(directory.Path, "x.tz-info"))));
        Assert.Throws<ArgumentNullException>(() => new TimeZoneSidecarWriter(null!, new RecordingSelector(), new FixedEncoder()));
        Assert.Throws<ArgumentNullException>(() => new TimeZoneSidecarWriter(new FixedCatalogReader(), null!, new FixedEncoder()));
        Assert.Throws<ArgumentNullException>(() => new TimeZoneSidecarWriter(new FixedCatalogReader(), new RecordingSelector(), null!));
    }

    private static string VendoredSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "eng", "toolchain.json")))
        {
            directory = directory.Parent;
        }
        return Path.Combine(directory!.FullName, "src", "NetWasm.Toolchain", "timezones", "zoneinfo");
    }

    private sealed class FixedCatalogReader : ITimeZoneCatalogReader
    {
        public TimeZoneCatalog Read(string sourceDirectory) => new(
            "2026c",
            ImmutableDictionary<string, TimeZoneDefinition>.Empty,
            ImmutableDictionary<string, string>.Empty);
    }

    private sealed class RecordingSelector : ITimeZoneSelectionSelector
    {
        public bool IncludeAll { get; private set; }

        public ImmutableArray<TimeZoneDefinition> Select(
            TimeZoneCatalog catalog,
            IReadOnlyCollection<string> requestedZones,
            bool includeAll)
        {
            IncludeAll = includeAll;
            return [];
        }
    }

    private sealed class FixedEncoder : ITimeZoneAssetEncoder
    {
        public TimeZoneAssetArtifact Encode(string dataVersion, ImmutableArray<TimeZoneDefinition> zones) =>
            new([1, 2, 3], "identity");
    }
}
