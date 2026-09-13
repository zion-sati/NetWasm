namespace NetWasm.Hosting.Build.Environment;

public interface IHostingBuildEnvironmentResolver
{
    HostingBuildEnvironment Resolve(HostingBuildEnvironmentRequest request);
}

public interface IToolchainPackagePathResolver
{
    ToolchainPackagePaths Resolve(
        string packageRoot,
        NetWasm.Toolchain.Prerequisites.ResolvedHostExecutable node,
        NetWasm.Toolchain.Prerequisites.ValidatedHostToolCompatibility nodeCompatibility);
}
