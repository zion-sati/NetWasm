namespace NetWasm.Toolchain.Host;

public sealed record HostPlatform(
    HostOperatingSystem OperatingSystem,
    HostArchitecture Architecture);

public sealed record HostRid(string Value);
