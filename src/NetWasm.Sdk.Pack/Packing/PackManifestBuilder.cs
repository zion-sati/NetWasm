using System.Collections.Immutable;
using System.Security.Cryptography;

namespace NetWasm.Sdk.Pack.Packing;

public sealed class PackManifestBuilder : IPackManifestBuilder
{
    private readonly IPackRequestFingerprintBuilder fingerprintBuilder;

    public PackManifestBuilder(IPackRequestFingerprintBuilder fingerprintBuilder)
    {
        this.fingerprintBuilder = fingerprintBuilder ?? throw new ArgumentNullException(nameof(fingerprintBuilder));
    }

    public CanonicalPackManifest Build(CanonicalPackage package, ArchivePlan plan, PackageOutput output, SymbolPackageOutput? symbols)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(output);
        var entryHashes = plan.Entries
            .OrderBy(static entry => entry.Path, StringComparer.Ordinal)
            .Select(entry => $"{entry.Path}={Hash(entry.Content.AsSpan())}")
            .ToImmutableArray();
        var inputHashes = plan.Entries
            .Where(static entry => entry.SourcePath is not null)
            .Select(entry => $"{entry.Path}={entry.Content.Length}:{Hash(entry.Content.AsSpan())}")
            .Order(StringComparer.Ordinal)
            .ToImmutableArray();
        var requestHash = fingerprintBuilder.Build(package);
        return new CanonicalPackManifest(
            "1",
            "0.1",
            requestHash,
            entryHashes,
            output.Sha256,
            symbols?.Sha256)
        {
            InputHashes = inputHashes,
            TargetKeys = package.TargetIdentities.Select(static target => target.Alias).Order(StringComparer.OrdinalIgnoreCase).ToImmutableArray(),
            PackagePath = Path.GetFileName(output.Path),
            SymbolPackagePath = symbols is null ? null : Path.GetFileName(symbols.Path)
        };
    }

    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
