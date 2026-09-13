namespace NetWasm.TimeZones;

internal sealed record TimeZoneDeploymentManifest(
    int SchemaVersion,
    string DataVersion,
    string Identity,
    string AssetFile,
    int UncompressedBytes,
    string BrotliFile,
    int BrotliBytes,
    string BrowserLoaderFile,
    string[] Zones);
