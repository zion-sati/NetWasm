// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Toolchain.Manifest;

public sealed record PinnedToolchainManifest
{
    public required string SchemaVersion { get; init; }

    public required string PackageId { get; init; }

    public required string PackageVersion { get; init; }

    public ImmutableArray<PinnedPlatformAssetDescriptor> Assets { get; init; }
}
