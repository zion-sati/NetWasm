// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.TimeZones;

namespace NetWasm.Hosting.Build.TimeZones;

/// <summary>Which zones a deployment's time zone sidecar carries.</summary>
public enum TimeZoneSidecarMode
{
    Selected,
    Full,
}

public sealed record TimeZoneSidecarRequest(
    string SourceRoot,
    TimeZoneSidecarMode Mode,
    IReadOnlyCollection<string> Zones,
    string OutputPath);

public sealed record TimeZoneSidecar(string DataVersion, string Identity, int ZoneCount);

public interface ITimeZoneSidecarWriter
{
    TimeZoneSidecar Write(TimeZoneSidecarRequest request);
}

/// <summary>
/// Writes the uncompressed payload a deployment ships as <c>&lt;program&gt;.wasm.tz-info</c>,
/// from the pinned Toolchain zone source, using the same generator as the
/// standalone time zone tool.
/// </summary>
public sealed class TimeZoneSidecarWriter : ITimeZoneSidecarWriter
{
    private readonly ITimeZoneCatalogReader _catalogs;
    private readonly ITimeZoneSelectionSelector _selections;
    private readonly ITimeZoneAssetEncoder _assets;

    public TimeZoneSidecarWriter()
        : this(
            new TimeZoneCatalogReader(
                new IanaVersionReader(),
                new TzifParser(new PosixFutureRuleExpander(new PosixRuleParser()))),
            new TimeZoneSelectionSelector(),
            new TimeZoneAssetEncoder())
    {
    }

    internal TimeZoneSidecarWriter(
        ITimeZoneCatalogReader catalogs,
        ITimeZoneSelectionSelector selections,
        ITimeZoneAssetEncoder assets)
    {
        _catalogs = catalogs ?? throw new ArgumentNullException(nameof(catalogs));
        _selections = selections ?? throw new ArgumentNullException(nameof(selections));
        _assets = assets ?? throw new ArgumentNullException(nameof(assets));
    }

    public TimeZoneSidecar Write(TimeZoneSidecarRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Zones);
        if (!Path.IsPathFullyQualified(request.SourceRoot) || !Path.IsPathFullyQualified(request.OutputPath))
        {
            throw new ArgumentException("Time zone sidecar paths must be absolute.", nameof(request));
        }
        if (request.Mode == TimeZoneSidecarMode.Full && request.Zones.Count != 0)
        {
            throw new ArgumentException(
                "NetWasmTimeZoneMode Full already ships every zone; remove the NetWasmTimeZone items.",
                nameof(request));
        }
        var catalog = _catalogs.Read(request.SourceRoot);
        var zones = _selections.Select(catalog, request.Zones, request.Mode == TimeZoneSidecarMode.Full);
        var asset = _assets.Encode(catalog.DataVersion, zones);
        Directory.CreateDirectory(Path.GetDirectoryName(request.OutputPath)!);
        File.WriteAllBytes(request.OutputPath, asset.Contents);
        return new(catalog.DataVersion, asset.Identity, zones.Length);
    }
}
