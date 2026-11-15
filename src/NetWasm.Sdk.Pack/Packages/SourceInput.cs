// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Sdk.Pack.Packages;

public sealed record SourceInput(string SourcePath, string TargetPath, string? SourceRoot = null)
{
    public string? TargetFrameworkAlias { get; init; }
    public string? ExpectedSha256 { get; init; }
    public long? ExpectedLength { get; init; }
}
