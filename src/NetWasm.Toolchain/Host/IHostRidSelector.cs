namespace NetWasm.Toolchain.Host;

public interface IHostRidSelector
{
    HostRid SelectRid(HostPlatform platform);
}
