// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Sdk.Pack.Restore;

public sealed record RestoreDependencyEvidence(
    string Id,
    string Version,
    string TargetFramework,
    bool IsPrivate,
    bool IsDevelopmentDependency)
{
    public string? VersionRange { get; init; }
    public string? ResolvedVersion { get; init; }
    public string? Source { get; init; }
}
