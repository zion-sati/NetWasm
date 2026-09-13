namespace NetWasm.Sdk.Pack.Archives;

public sealed record PackArtifacts(
    string? NuspecPath,
    byte[] NuspecBytes,
    string? ManifestPath,
    byte[] ManifestBytes);
