// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;

namespace NetWasm.Toolchain.Resolution;

public sealed class UnsupportedPlatformAssetException(string assetId) :
    InvalidOperationException($"The Toolchain package does not declare platform asset '{assetId}'.");
