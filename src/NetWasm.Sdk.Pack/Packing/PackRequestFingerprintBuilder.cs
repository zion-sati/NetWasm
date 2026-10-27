using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NetWasm.Sdk.Pack.Packing;

/// <summary>
/// Builds the privacy-safe, content-sensitive identity of one canonical pack
/// request. The same strategy is used before a no-build reuse and when the
/// manifest is persisted.
/// </summary>
public sealed class PackRequestFingerprintBuilder : IPackRequestFingerprintBuilder
{
    private readonly IPackageFileReader fileReader;

    public PackRequestFingerprintBuilder(IPackageFileReader fileReader)
    {
        this.fileReader = fileReader ?? throw new ArgumentNullException(nameof(fileReader));
    }

    public string Build(CanonicalPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        var files = BuildFileFingerprints(package.Files, package.Determinism);
        var symbols = BuildSymbolFingerprints(package.Symbols.Files, package.Determinism);
        PackSourceFileFingerprint[] source = [.. package.Source.Files
            .OrderBy(static file => file.TargetPath, StringComparer.Ordinal)
            .Select(file => new PackSourceFileFingerprint(
                file.TargetPath,
                file.TargetFrameworkAlias,
                file.ExpectedSha256,
                file.ExpectedLength,
                ReadHash(file.SourcePath, package.Determinism.MaxEntryBytes)))];
        var metadata = package.Metadata;
        var request = new PackRequestFingerprint(
            "1",
            "0.1",
            package.Identity,
            Path.GetFileName(package.OutputPath),
            Path.GetFileName(package.NuspecOutputPath ?? string.Empty),
            Path.GetFileName(package.ManifestOutputPath ?? string.Empty),
            new PackMetadataFingerprint(
                metadata.Authors,
                metadata.Description,
                metadata.Title,
                metadata.Owners,
                metadata.Summary,
                metadata.PublishRepositoryUrl ? metadata.ProjectUrl : null,
                metadata.LicenseExpression,
                metadata.LicenseFile,
                metadata.Icon,
                metadata.Readme,
                metadata.Copyright,
                metadata.Tags,
                metadata.ReleaseNotes,
                metadata.PublishRepositoryUrl ? metadata.RepositoryUrl : null,
                metadata.PublishRepositoryUrl ? metadata.RepositoryType : null,
                metadata.PublishRepositoryUrl ? metadata.RepositoryBranch : null,
                metadata.PublishRepositoryUrl ? metadata.RepositoryCommit : null,
                metadata.PackageTypes,
                metadata.DevelopmentDependency,
                metadata.Serviceable,
                metadata.PublishRepositoryUrl),
            [.. package.Targets
                .OrderBy(static target => target.Alias, StringComparer.OrdinalIgnoreCase)
                .Select(static target => new PackTargetFingerprint(
                    target.Alias,
                    target.Identifier,
                    target.Version,
                    target.CanonicalFolder,
                    target.CanonicalDependencyGroup))],
            [.. package.TargetIdentities.OrderBy(static target => target.Alias, StringComparer.OrdinalIgnoreCase)],
            files,
            [.. package.DependencyGroups
                .OrderBy(static group => group.TargetFramework, StringComparer.Ordinal)
                .Select(static group => new PackDependencyGroupFingerprint(
                    group.TargetFramework,
                    group.TargetFrameworkAlias,
                    [.. group.Dependencies
                        .OrderBy(static dependency => dependency.Id, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(static dependency => dependency.VersionRange, StringComparer.Ordinal)]))],
            new PackSymbolsFingerprint(package.Symbols.IncludeSymbols, package.Symbols.Format, symbols),
            new PackSourceFingerprint(
                package.Source.IncludeSource,
                package.Source.PublishRepositoryUrl,
                string.IsNullOrWhiteSpace(package.Source.SourceLinkJson)
                    ? null
                    : Hash(Encoding.UTF8.GetBytes(package.Source.SourceLinkJson)),
                source),
            new PackRestoreFingerprint(
                package.Restore.AssetsFileHash,
                package.Restore.LockFileHash,
                [.. package.Restore.TargetKeys.Order(StringComparer.Ordinal)],
                package.Restore.NuGetVersion,
                package.Restore.SdkVersion,
                package.Restore.Required,
                [.. package.Restore.Graph
                    .OrderBy(static edge => edge.Source, StringComparer.Ordinal)
                    .ThenBy(static edge => edge.TargetFramework, StringComparer.Ordinal)
                    .ThenBy(static edge => edge.Id, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(static edge => edge.Version, StringComparer.Ordinal)
                    .Select(static edge => new PackRestoreEdgeFingerprint(
                        edge.Id,
                        edge.Version,
                        edge.VersionRange,
                        edge.ResolvedVersion,
                        edge.TargetFramework,
                        edge.IsPrivate,
                        edge.IsDevelopmentDependency,
                        edge.Source))]),
            package.Determinism,
            package.SuppressDependencies);

        return Hash(JsonSerializer.SerializeToUtf8Bytes(request, SdkPackJsonContext.Default.PackRequestFingerprint));
    }

    private PackFileFingerprint[] BuildFileFingerprints(
        IEnumerable<CanonicalPackageFile> packageFiles,
        DeterminismPolicy policy)
    {
        var result = new List<PackFileFingerprint>();
        var totalBytes = 0L;
        foreach (var file in packageFiles.OrderBy(static file => file.TargetPath, StringComparer.Ordinal))
        {
            var remainingBytes = policy.MaxArchiveBytes - totalBytes;
            if (remainingBytes <= 0)
            {
                throw new NetWasmPackException(NetWasmPackErrorCode.NWPK014, "The package archive exceeds the deterministic size policy.");
            }

            var bytes = fileReader.Read(file.SourcePath, Math.Min(policy.MaxEntryBytes, remainingBytes));
            totalBytes += bytes.LongLength;
            result.Add(new PackFileFingerprint(
                file.TargetPath,
                file.Kind,
                file.TargetFrameworkAlias,
                file.ExpectedSha256,
                file.ExpectedLength,
                Hash(bytes)));
        }

        return result.ToArray();
    }

    private PackSymbolFileFingerprint[] BuildSymbolFingerprints(
        IEnumerable<SymbolInput> symbolFiles,
        DeterminismPolicy policy)
    {
        var result = new List<PackSymbolFileFingerprint>();
        var totalBytes = 0L;
        foreach (var file in symbolFiles.OrderBy(static file => file.TargetPath, StringComparer.Ordinal))
        {
            var remainingBytes = policy.MaxArchiveBytes - totalBytes;
            if (remainingBytes <= 0)
            {
                throw new NetWasmPackException(NetWasmPackErrorCode.NWPK014, "The symbol archive exceeds the deterministic size policy.");
            }

            var bytes = fileReader.Read(file.SourcePath, Math.Min(policy.MaxEntryBytes, remainingBytes));
            totalBytes += bytes.LongLength;
            result.Add(new PackSymbolFileFingerprint(
                file.TargetPath,
                file.Format,
                file.TargetFrameworkAlias,
                file.ExpectedSha256,
                file.ExpectedLength,
                Hash(bytes)));
        }

        return result.ToArray();
    }

    private string ReadHash(string sourcePath, long maxLength)
    {
        var bytes = fileReader.Read(sourcePath, maxLength);
        return Hash(bytes);
    }

    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

}
