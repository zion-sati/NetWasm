namespace NetWasm.Toolchain.Manifest;

public sealed record PinnedPlatformAssetDescriptor
{
    public required string Id { get; init; }

    public required string Version { get; init; }

    public required string RelativePath { get; init; }

    public required string Sha256 { get; init; }
}

public sealed record ResolvedPlatformAsset(
    string PackageId,
    string PackageVersion,
    string Id,
    string Version,
    string AbsolutePath);
