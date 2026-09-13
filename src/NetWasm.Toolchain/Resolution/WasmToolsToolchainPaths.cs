using System;
using NetWasm.Toolchain.Prerequisites;

namespace NetWasm.Toolchain.Resolution;

public sealed record WasmToolsToolchainPaths(
    string PackageId,
    string PackageVersion,
    string WasmToolsVersion,
    string NodePath,
    Version NodeVersion,
    string CommandPath,
    string ModulePath,
    string ApacheLicensePath,
    string ApacheLlvmLicensePath,
    string MitLicensePath,
    string ReadmePath);

public interface IWasmToolsToolchainPathResolver
{
    WasmToolsToolchainPaths Resolve(
        ResolvedHostExecutable nodeExecutable,
        ValidatedHostToolCompatibility nodeCompatibility);
}
