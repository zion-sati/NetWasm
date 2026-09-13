using System;
using NetWasm.Toolchain.Prerequisites;

namespace NetWasm.Toolchain.Resolution;

public sealed record JcoToolchainPaths(
    string PackageId,
    string PackageVersion,
    string JcoVersion,
    string Preview2ShimVersion,
    string NodePath,
    Version NodeVersion,
    string EntryPointPath,
    string PackageLockPath,
    string ClosureIntegrityPath,
    string NoticesPath,
    string ClosurePolicyPath);

public interface IJcoToolchainPathResolver
{
    JcoToolchainPaths Resolve(
        ResolvedHostExecutable nodeExecutable,
        ValidatedHostToolCompatibility nodeCompatibility);
}
