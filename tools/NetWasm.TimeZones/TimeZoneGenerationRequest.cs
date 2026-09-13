using System.Collections.Immutable;

namespace NetWasm.TimeZones;

internal sealed record TimeZoneGenerationRequest(
    string SourceDirectory,
    string AssetPath,
    string BrotliPath,
    string ManifestPath,
    string BrowserLoaderPath,
    ImmutableArray<string> Zones,
    bool IncludeAll);
