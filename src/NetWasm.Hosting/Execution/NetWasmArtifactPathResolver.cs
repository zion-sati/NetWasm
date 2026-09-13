using System;
using System.IO;

namespace NetWasm.Hosting.Execution;

internal sealed class NetWasmArtifactPathResolver : INetWasmArtifactPathResolver
{
    private const string DescriptorSuffix = ".netwasm.execution.json";
    private const string RequestSuffix = ".netwasm.request.json";

    public NetWasmArtifactPaths Resolve(string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        if (sourcePath.Contains('\0', StringComparison.Ordinal)
            || !Path.IsPathFullyQualified(sourcePath)
            || !string.Equals(Path.GetFullPath(sourcePath), sourcePath, PathComparison))
        {
            throw new ArgumentException(
                "A NetWasm artifact source path must be canonical and fully qualified.",
                nameof(sourcePath));
        }

        var extension = Path.GetExtension(sourcePath);
        if (extension.Length == 0)
        {
            throw new ArgumentException(
                "A NetWasm artifact source path must have a file extension.",
                nameof(sourcePath));
        }

        var stem = sourcePath[..^extension.Length];
        return new NetWasmArtifactPaths(
            sourcePath,
            stem + DescriptorSuffix,
            stem + RequestSuffix);
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
