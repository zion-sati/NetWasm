// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Toolchain.Manifest;

namespace NetWasm.Toolchain.Resolution;

public interface IPinnedPlatformAssetPathResolver
{
    ResolvedPlatformAsset Resolve(string assetId);
}
