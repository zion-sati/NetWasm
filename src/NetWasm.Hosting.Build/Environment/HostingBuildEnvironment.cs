using NetWasm.Toolchain.Prerequisites;
using NetWasm.Toolchain.Resolution;

namespace NetWasm.Hosting.Build.Environment;

public sealed record HostingBuildEnvironmentRequest(string ToolchainPackageRoot);

public sealed record ToolchainPackagePaths(
    string PackageId,
    string PackageVersion,
    string ManifestPath,
    WitPackagePaths WitPackages,
    string Preview2ShimRoot,
    WasmToolsToolchainPaths WasmTools,
    RawInspectionToolchainPaths RawInspection,
    JcoToolchainPaths Jco,
    HostingBundleToolchainPaths HostingBundle);

public sealed record HostingBuildEnvironment(
    ResolvedHostExecutable Node,
    ValidatedHostToolCompatibility NodeCompatibility,
    ResolvedHostExecutable WasmLd,
    ValidatedHostToolCompatibility WasmLdCompatibility,
    ToolchainPackagePaths Toolchain);
