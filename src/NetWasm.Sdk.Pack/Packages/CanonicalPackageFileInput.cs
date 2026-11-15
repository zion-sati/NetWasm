// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Sdk.Pack.Packages;

public sealed record CanonicalPackageFileInput(string SourcePath, string TargetPath)
{
    public string? TargetFrameworkAlias { get; init; }
    public PackageFileKind Kind { get; init; } = PackageFileKind.Other;
    public bool ExcludeFromMainPackage { get; init; }
    public string? SourceRoot { get; init; }
    public string? ExpectedSha256 { get; init; }
    public long? ExpectedLength { get; init; }
}
