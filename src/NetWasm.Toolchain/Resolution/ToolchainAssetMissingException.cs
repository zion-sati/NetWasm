// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;

namespace NetWasm.Toolchain.Resolution;

public sealed class ToolchainAssetMissingException(string path)
    : InvalidOperationException($"Pinned NetWasm toolchain asset is missing: '{path}'.");
