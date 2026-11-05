using NetWasm.Toolchain.Prerequisites;
using NetWasm.Toolchain.Resolution;

namespace NetWasm.Hosting.Build.Environment;

public sealed record HostingBuildEnvironmentRequest(
    string ToolchainPackageRoot,
    string? HostToolsPackageRoot,
    string? HostToolsPackageId,
    string? HostToolsPackageVersion,
    string? HostRid)
{
    public HostingBuildEnvironmentRequest(string toolchainPackageRoot)
        : this(toolchainPackageRoot, null, null, null, null)
    {
    }
}

public sealed record ToolchainPackagePaths(
    string PackageId,
    string PackageVersion,
    string ManifestPath,
    WitPackagePaths WitPackages,
    string Preview2ShimRoot,
    WasmToolsToolchainPaths WasmTools,
    RawInspectionToolchainPaths RawInspection,
    JcoToolchainPaths Jco,
    HostingBundleToolchainPaths HostingBundle,
    TimeZoneSourcePaths TimeZones);

public sealed record NativeBinaryenTool(
    ResolvedHostExecutable Executable,
    ValidatedHostToolCompatibility Compatibility);

public sealed record NativeBinaryenTools(
    NativeBinaryenTool? WasmOpt);

// A native wasm-tools of the toolchain's wasm-tools version, which the build runs
// in place of the toolchain's WASI build of it.
public sealed record NativeWasmTools(
    ResolvedHostExecutable Executable,
    ValidatedHostToolCompatibility Compatibility);

public sealed record HostingBuildEnvironment(
    ResolvedHostExecutable Node,
    ValidatedHostToolCompatibility NodeCompatibility,
    ResolvedHostExecutable WasmLd,
    ValidatedHostToolCompatibility WasmLdCompatibility,
    NativeBinaryenTools Binaryen,
    ToolchainPackagePaths Toolchain)
{
    // nwc, the NetWasm compiler, from the restored host tools; null when the tools
    // come from the host or predate it.
    public string? CompilerPath { get; init; }

    // Null when neither the restored host tools nor NETWASM_WASM_TOOLS_PATH provide it.
    public NativeWasmTools? NativeWasmTools { get; init; }
}
