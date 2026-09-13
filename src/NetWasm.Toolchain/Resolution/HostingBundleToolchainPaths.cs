using System;
using NetWasm.Toolchain.Prerequisites;

namespace NetWasm.Toolchain.Resolution;

public sealed record HostingBundleToolchainPaths(
    string PackageId,
    string PackageVersion,
    string RolldownVersion,
    string NodePath,
    Version NodeVersion,
    string CommandPath,
    string EntryPointPath,
    string PackageLockPath,
    string ClosureIntegrityPath,
    string NoticesPath,
    string ClosurePolicyPath);

public interface IHostingBundleToolchainPathResolver
{
    HostingBundleToolchainPaths Resolve(
        ResolvedHostExecutable nodeExecutable,
        ValidatedHostToolCompatibility nodeCompatibility);
}
