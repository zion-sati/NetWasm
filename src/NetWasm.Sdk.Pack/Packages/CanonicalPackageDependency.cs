// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Sdk.Pack.Packages;

public sealed record CanonicalPackageDependency(string Id, string VersionRange)
{
    public string IncludeAssets { get; init; } = "all";
    public string ExcludeAssets { get; init; } = "none";
    public string PrivateAssets { get; init; } = "none";
}
