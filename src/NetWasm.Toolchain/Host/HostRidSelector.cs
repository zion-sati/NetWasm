using System;

namespace NetWasm.Toolchain.Host;

public sealed class HostRidSelector : IHostRidSelector
{
    public HostRid SelectRid(HostPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(platform);

        return platform switch
        {
            { OperatingSystem: HostOperatingSystem.MacOS, Architecture: HostArchitecture.Arm64 } => new("osx-arm64"),
            { OperatingSystem: HostOperatingSystem.MacOS, Architecture: HostArchitecture.X64 } => new("osx-x64"),
            { OperatingSystem: HostOperatingSystem.Linux, Architecture: HostArchitecture.Arm64 } => new("linux-arm64"),
            { OperatingSystem: HostOperatingSystem.Linux, Architecture: HostArchitecture.X64 } => new("linux-x64"),
            { OperatingSystem: HostOperatingSystem.Windows, Architecture: HostArchitecture.Arm64 } => new("win-arm64"),
            { OperatingSystem: HostOperatingSystem.Windows, Architecture: HostArchitecture.X64 } => new("win-x64"),
            _ => throw new UnsupportedHostRidException(
                $"{platform.OperatingSystem}-{platform.Architecture}"),
        };
    }
}
