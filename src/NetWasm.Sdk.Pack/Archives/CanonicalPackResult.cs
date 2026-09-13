namespace NetWasm.Sdk.Pack.Archives;

public sealed record CanonicalPackResult
{
    public CanonicalPackResult(
        PackageOutput package,
        SymbolPackageOutput? symbols,
        CanonicalPackManifest manifest,
        string? nuspecPath = null,
        string? manifestPath = null)
    {
        Package = package;
        Symbols = symbols;
        Manifest = manifest;
        NuspecPath = nuspecPath;
        ManifestPath = manifestPath;
    }

    public PackageOutput Package { get; }
    public SymbolPackageOutput? Symbols { get; }
    public CanonicalPackManifest Manifest { get; }
    public string? NuspecPath { get; }
    public string? ManifestPath { get; }
}
