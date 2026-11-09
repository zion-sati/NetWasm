using System;
using System.Collections.Immutable;
using NetWasm.Toolchain.Prerequisites;

namespace NetWasm.Toolchain.Resolution;

public sealed record JcoToolchainPaths(
    string PackageId,
    string PackageVersion,
    string JcoVersion,
    string Preview3ShimVersion,
    string NodePath,
    Version NodeVersion,
    string EntryPointPath,
    string PackageLockPath,
    string ClosureIntegrityPath,
    string NoticesPath,
    string ClosurePolicyPath,
    NodeHttpProviderPaths NodeHttpProvider);

// The Node.js wasi:http client's files, under Root as a deployment lays them out.
public sealed record NodeHttpProviderPaths(
    string Root,
    string EntryPointPath,
    ImmutableArray<string> FilePaths);

public interface IJcoToolchainPathResolver
{
    JcoToolchainPaths Resolve(
        ResolvedHostExecutable nodeExecutable,
        ValidatedHostToolCompatibility nodeCompatibility);
}
