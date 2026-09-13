using System;

namespace NetWasm.Toolchain.Host;

public sealed class UnsupportedHostRidException(string platform)
    : InvalidOperationException($"No pinned NetWasm toolchain is available for host '{platform}'.");
