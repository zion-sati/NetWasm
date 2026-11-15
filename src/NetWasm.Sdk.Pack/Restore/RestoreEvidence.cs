// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

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
