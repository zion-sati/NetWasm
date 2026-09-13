namespace NetWasm.TimeZones;

internal sealed class TimeZoneGenerationCommand(
    ITimeZoneCatalogReader catalogs,
    ITimeZoneSelectionSelector selections,
    ITimeZoneAssetEncoder assets,
    IBrotliAssetCompressor brotli,
    ITimeZoneManifestEncoder manifests,
    ITimeZoneBrowserLoaderEncoder browserLoaders,
    IArtifactWriter artifacts) : ITimeZoneGenerationCommand
{
    private readonly ITimeZoneCatalogReader _catalogs =
        catalogs ?? throw new ArgumentNullException(nameof(catalogs));
    private readonly ITimeZoneSelectionSelector _selections =
        selections ?? throw new ArgumentNullException(nameof(selections));
    private readonly ITimeZoneAssetEncoder _assets =
        assets ?? throw new ArgumentNullException(nameof(assets));
    private readonly IBrotliAssetCompressor _brotli =
        brotli ?? throw new ArgumentNullException(nameof(brotli));
    private readonly ITimeZoneManifestEncoder _manifests =
        manifests ?? throw new ArgumentNullException(nameof(manifests));
    private readonly ITimeZoneBrowserLoaderEncoder _browserLoaders =
        browserLoaders ?? throw new ArgumentNullException(nameof(browserLoaders));
    private readonly IArtifactWriter _artifacts =
        artifacts ?? throw new ArgumentNullException(nameof(artifacts));

    public TimeZoneDeploymentManifest Execute(TimeZoneGenerationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var catalog = _catalogs.Read(request.SourceDirectory);
        var zones = _selections.Select(catalog, request.Zones, request.IncludeAll);
        var asset = _assets.Encode(catalog.DataVersion, zones);
        var compressed = _brotli.Compress(asset.Contents);
        var manifest = new TimeZoneDeploymentManifest(
            1,
            catalog.DataVersion,
            asset.Identity,
            Path.GetFileName(request.AssetPath),
            asset.Contents.Length,
            Path.GetFileName(request.BrotliPath),
            compressed.Length,
            Path.GetFileName(request.BrowserLoaderPath),
            [.. zones.Select(zone => zone.Name)]);
        _artifacts.Write(request.AssetPath, asset.Contents);
        _artifacts.Write(request.BrotliPath, compressed);
        _artifacts.Write(request.ManifestPath, _manifests.Encode(manifest));
        _artifacts.Write(
            request.BrowserLoaderPath,
            _browserLoaders.Encode(Path.GetFileName(request.AssetPath)));
        return manifest;
    }
}
