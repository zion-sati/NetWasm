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
        var source = package.Source.Files
            .OrderBy(static file => file.TargetPath, StringComparer.Ordinal)
            .Select(file => new
            {
                file.TargetPath,
                file.TargetFrameworkAlias,
                file.ExpectedSha256,
                file.ExpectedLength,
                ContentHash = ReadHash(file.SourcePath, package.Determinism.MaxEntryBytes)
            });
        var request = new
        {
            schema = "1",
            task = "0.1",
            identity = package.Identity,
            output = Path.GetFileName(package.OutputPath),
            nuspecOutput = Path.GetFileName(package.NuspecOutputPath ?? string.Empty),
            manifestOutput = Path.GetFileName(package.ManifestOutputPath ?? string.Empty),
            metadata = new
            {
                package.Metadata.Authors,
                package.Metadata.Description,
                package.Metadata.Title,
                package.Metadata.Owners,
                package.Metadata.Summary,
                ProjectUrl = package.Metadata.PublishRepositoryUrl ? package.Metadata.ProjectUrl : null,
                package.Metadata.LicenseExpression,
                package.Metadata.LicenseFile,
                package.Metadata.Icon,
                package.Metadata.Readme,
                package.Metadata.Copyright,
                package.Metadata.Tags,
                package.Metadata.ReleaseNotes,
                RepositoryUrl = package.Metadata.PublishRepositoryUrl ? package.Metadata.RepositoryUrl : null,
                RepositoryType = package.Metadata.PublishRepositoryUrl ? package.Metadata.RepositoryType : null,
                RepositoryBranch = package.Metadata.PublishRepositoryUrl ? package.Metadata.RepositoryBranch : null,
                RepositoryCommit = package.Metadata.PublishRepositoryUrl ? package.Metadata.RepositoryCommit : null,
                package.Metadata.PackageTypes,
                package.Metadata.DevelopmentDependency,
                package.Metadata.Serviceable,
                package.Metadata.PublishRepositoryUrl
            },
            targets = package.Targets
                .OrderBy(static target => target.Alias, StringComparer.OrdinalIgnoreCase)
                .Select(static target => new
                {
                    target.Alias,
                    target.Identifier,
                    target.Version,
                    target.CanonicalFolder,
                    target.CanonicalDependencyGroup
                }),
            targetIdentities = package.TargetIdentities
                .OrderBy(static target => target.Alias, StringComparer.OrdinalIgnoreCase),
            files,
            dependencies = package.DependencyGroups
                .OrderBy(static group => group.TargetFramework, StringComparer.Ordinal)
                .Select(static group => new
                {
                    group.TargetFramework,
                    group.TargetFrameworkAlias,
                    Dependencies = group.Dependencies
                        .OrderBy(static dependency => dependency.Id, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(static dependency => dependency.VersionRange, StringComparer.Ordinal)
                }),
            symbols = new
            {
                package.Symbols.IncludeSymbols,
                package.Symbols.Format,
                Files = symbols
            },
            source = new
            {
                package.Source.IncludeSource,
                package.Source.PublishRepositoryUrl,
                SourceLinkHash = string.IsNullOrWhiteSpace(package.Source.SourceLinkJson)
                    ? null
                    : Hash(Encoding.UTF8.GetBytes(package.Source.SourceLinkJson)),
                Files = source
            },
            restore = new
            {
                package.Restore.AssetsFileHash,
                package.Restore.LockFileHash,
                TargetKeys = package.Restore.TargetKeys.Order(StringComparer.Ordinal),
                package.Restore.NuGetVersion,
                package.Restore.SdkVersion,
                package.Restore.Required,
                Graph = package.Restore.Graph
                    .OrderBy(static edge => edge.Source, StringComparer.Ordinal)
                    .ThenBy(static edge => edge.TargetFramework, StringComparer.Ordinal)
                    .ThenBy(static edge => edge.Id, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(static edge => edge.Version, StringComparer.Ordinal)
                    .Select(static edge => new
                    {
                        edge.Id,
                        edge.Version,
                        edge.VersionRange,
                        edge.ResolvedVersion,
                        edge.TargetFramework,
                        edge.IsPrivate,
                        edge.IsDevelopmentDependency,
                        edge.Source
                    })
            },
            determinism = package.Determinism,
            package.SuppressDependencies
        };

        return Hash(JsonSerializer.SerializeToUtf8Bytes(request));
    }

    private FileFingerprint[] BuildFileFingerprints(
        IEnumerable<CanonicalPackageFile> packageFiles,
        DeterminismPolicy policy)
    {
        var result = new List<FileFingerprint>();
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
            result.Add(new FileFingerprint(
                file.TargetPath,
                file.Kind,
                file.TargetFrameworkAlias,
                file.ExpectedSha256,
                file.ExpectedLength,
                Hash(bytes)));
        }

        return result.ToArray();
    }

    private FileSymbolFingerprint[] BuildSymbolFingerprints(
        IEnumerable<SymbolInput> symbolFiles,
        DeterminismPolicy policy)
    {
        var result = new List<FileSymbolFingerprint>();
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
            result.Add(new FileSymbolFingerprint(
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

    private sealed record FileFingerprint(
        string TargetPath,
        PackageFileKind Kind,
        string? TargetFrameworkAlias,
        string? ExpectedSha256,
        long? ExpectedLength,
        string ContentHash);

    private sealed record FileSymbolFingerprint(
        string TargetPath,
        string Format,
        string? TargetFrameworkAlias,
        string? ExpectedSha256,
        long? ExpectedLength,
        string ContentHash);
}
