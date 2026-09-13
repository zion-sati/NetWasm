namespace NetWasm.Toolchain.Prerequisites;

public interface IHostExecutablePathResolver
{
    ResolvedHostExecutable Resolve(HostExecutableResolutionRequest request);
}
