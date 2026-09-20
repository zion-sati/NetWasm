using System;
using System.Collections.Immutable;

namespace NetWasm.Toolchain.Prerequisites;

public sealed record HostExecutablePathConvention(
    char PathSeparator,
    string ExecutableSuffix,
    StringComparison PathComparison);

public sealed record HostExecutableRootFallback(
    string RootEnvironmentVariableName,
    string RelativeDirectory);

public sealed record HostExecutableEnvironmentFallback(
    string EnvironmentVariableName);

public sealed record HostExecutableResolutionRequest(
    string ToolId,
    string ExecutableName,
    string OverrideEnvironmentVariableName,
    ImmutableArray<HostExecutableEnvironmentFallback> EnvironmentFallbacks,
    ImmutableArray<HostExecutableRootFallback> RootFallbacks,
    bool SearchPath = true);

public enum HostExecutableResolutionSource
{
    Path,
    Override,
    EnvironmentExecutable,
    EnvironmentRoot,
    Package,
}

public sealed record ResolvedHostExecutable(
    string ToolId,
    string AbsolutePath,
    HostExecutableResolutionSource Source);
