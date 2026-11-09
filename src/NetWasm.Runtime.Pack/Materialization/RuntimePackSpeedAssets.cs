namespace NetWasm.Runtime.Pack.Materialization;

internal sealed record RuntimePackSpeedAssets(
    long RuntimeFootprintBytes,
    RuntimePackAsset RuntimeArchive,
    RuntimePackAsset CollectorArchive)
{
    public RuntimePackPlatformArchives? PlatformArchives { get; init; }
}
