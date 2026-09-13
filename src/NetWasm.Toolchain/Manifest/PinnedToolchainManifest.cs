using System.Collections.Immutable;

namespace NetWasm.Toolchain.Manifest;

public sealed record PinnedToolchainManifest
{
    public required string SchemaVersion { get; init; }

    public required string PackageId { get; init; }

    public required string PackageVersion { get; init; }

    public ImmutableArray<PinnedPlatformAssetDescriptor> Assets { get; init; }
}
