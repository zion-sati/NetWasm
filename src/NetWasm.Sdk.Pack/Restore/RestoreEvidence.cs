using System.Collections.Immutable;

namespace NetWasm.Sdk.Pack.Restore;

public sealed record RestoreEvidence(
    string AssetsFilePath,
    string AssetsFileHash,
    string? LockFilePath,
    string? LockFileHash,
    ImmutableArray<string> TargetKeys,
    string NuGetVersion,
    string SdkVersion)
{
    public ImmutableArray<RestoreDependencyEvidence> Graph { get; init; } = [];
    public bool Required { get; init; }

    public static RestoreEvidence Unspecified { get; } = new(
        string.Empty,
        string.Empty,
        null,
        null,
        ImmutableArray<string>.Empty,
        string.Empty,
        string.Empty);
}
