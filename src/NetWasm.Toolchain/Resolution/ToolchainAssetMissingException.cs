using System;

namespace NetWasm.Toolchain.Resolution;

public sealed class ToolchainAssetMissingException(string path)
    : InvalidOperationException($"Pinned NetWasm toolchain asset is missing: '{path}'.");
