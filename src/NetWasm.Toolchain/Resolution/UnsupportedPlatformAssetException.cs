using System;

namespace NetWasm.Toolchain.Resolution;

public sealed class UnsupportedPlatformAssetException(string assetId) :
    InvalidOperationException($"The Toolchain package does not declare platform asset '{assetId}'.");
