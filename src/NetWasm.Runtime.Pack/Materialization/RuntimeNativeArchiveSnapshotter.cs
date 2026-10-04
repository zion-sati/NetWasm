using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

namespace NetWasm.Runtime.Pack.Materialization;

internal sealed class RuntimeNativeArchiveSnapshotter(IRuntimeNativeArchiveReader archives) : IRuntimeNativeArchiveSnapshotter
{
    private readonly IRuntimeNativeArchiveReader _archives = archives ?? throw new ArgumentNullException(nameof(archives));

    public ImmutableArray<RuntimeNativeArchiveSnapshot> Snapshot(RuntimeNativeArchiveSnapshotRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DirectoryPath);
        if (!Path.IsPathFullyQualified(request.DirectoryPath) || request.Providers.IsDefault ||
            request.Providers.Any(provider => provider is null || !Path.IsPathFullyQualified(provider.Path) ||
                provider.Sha256 is not { Length: 64 } || !provider.Sha256.All(Uri.IsHexDigit)) ||
            request.Providers.DistinctBy(provider => provider.Path, StringComparer.Ordinal).Count() != request.Providers.Length)
            throw new InvalidOperationException("Native archive snapshots require unique absolute provider identities and an owned directory.");

        var result = ImmutableArray.CreateBuilder<RuntimeNativeArchiveSnapshot>(request.Providers.Length);
        for (var index = 0; index < request.Providers.Length; index++)
        {
            var provider = request.Providers[index];
            var snapshot = Path.Combine(request.DirectoryPath, $"native-{index}.a");
            File.Copy(provider.Path, snapshot, overwrite: false);
            if (!string.Equals(_archives.Read(snapshot), provider.Sha256, StringComparison.Ordinal))
                throw new InvalidOperationException("A selected native archive changed before its verified snapshot was created.");
            result.Add(new(provider, snapshot));
        }
        return result.MoveToImmutable();
    }
}
