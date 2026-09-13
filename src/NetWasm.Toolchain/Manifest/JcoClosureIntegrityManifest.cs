using System.Collections.Immutable;

namespace NetWasm.Toolchain.Manifest;

public sealed record JcoClosureIntegrityManifest
{
    public required string SchemaVersion { get; init; }

    public ImmutableArray<JcoClosureIntegrityEntry> Files { get; init; }
}

public sealed record JcoClosureIntegrityEntry
{
    public required string Path { get; init; }

    public required string Sha256 { get; init; }
}
