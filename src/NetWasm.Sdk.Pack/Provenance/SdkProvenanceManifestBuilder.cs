using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using NetWasm.Sdk.Pack.Archives;
using NetWasm.Sdk.Pack.Packages;

namespace NetWasm.Sdk.Pack.Provenance;

public sealed class SdkProvenanceManifestBuilder : ISdkProvenanceManifestBuilder
{
    private const long MaxFileBytes = 512L * 1024 * 1024;
    private readonly IPackageFileReader fileReader;

    public SdkProvenanceManifestBuilder(IPackageFileReader fileReader)
    {
        this.fileReader = fileReader ?? throw new ArgumentNullException(nameof(fileReader));
    }

    public byte[] Build(SdkProvenanceManifestInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ValidateRequired(input.PackageId, "The SDK provenance package ID is missing.");
        ValidateRequired(input.PackageVersion, "The SDK provenance package version is missing.");
        var sourceRevision = NormalizeRevision(input.SourceRevision);
        ValidatePath(input.ManifestPackagePath);
        ArgumentNullException.ThrowIfNull(input.PackageFiles);
        ArgumentNullException.ThrowIfNull(input.SourceFiles);

        var packageEntries = ReadEntries(input.PackageFiles, includeLogicalSourcePath: false);
        var sourceEntries = ReadEntries(input.SourceFiles, includeLogicalSourcePath: true);
        var document = new ManifestDocument(
            1,
            input.PackageId,
            input.PackageVersion,
            sourceRevision,
            input.ManifestPackagePath,
            "sha256",
            "excluded-from-content-digests",
            packageEntries,
            sourceEntries,
            Digest(packageEntries),
            Digest(sourceEntries));
        return JsonSerializer.SerializeToUtf8Bytes(document, Options);
    }

    private ManifestEntry[] ReadEntries(
        IEnumerable<SdkProvenanceFileInput> files,
        bool includeLogicalSourcePath)
    {
        var entries = files
            .Select(file => ReadEntry(file, includeLogicalSourcePath))
            .OrderBy(entry => entry.Path, StringComparer.Ordinal)
            .ToArray();
        if (entries.Length == 0)
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK011, "The SDK provenance input set is empty.");
        }

        if (entries.Select(static entry => entry.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Length)
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK008, "The SDK provenance input set contains a duplicate path.");
        }

        if (entries.Select(static entry => entry.PackagePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Length)
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK008, "The SDK provenance input set contains a duplicate package path.");
        }

        return entries;
    }

    private ManifestEntry ReadEntry(SdkProvenanceFileInput file, bool includeLogicalSourcePath)
    {
        ArgumentNullException.ThrowIfNull(file);
        ValidatePath(file.PackagePath);
        if (includeLogicalSourcePath)
        {
            ValidatePath(file.LogicalSourcePath);
        }

        var bytes = fileReader.Read(file.SourcePath, MaxFileBytes);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return new ManifestEntry(
            includeLogicalSourcePath ? file.LogicalSourcePath! : file.PackagePath,
            file.PackagePath,
            bytes.LongLength,
            hash);
    }

    private static string Digest(IEnumerable<ManifestEntry> entries)
    {
        var canonical = new StringBuilder();
        foreach (var entry in entries)
        {
            canonical
                .Append(entry.Path).Append('\n')
                .Append(entry.PackagePath).Append('\n')
                .Append(entry.Length).Append('\n')
                .Append(entry.Sha256).Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()))).ToLowerInvariant();
    }

    private static void ValidateRequired(string value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK002, message);
        }
    }

    private static string NormalizeRevision(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length is not (40 or 64) || value.Any(static character => !Uri.IsHexDigit(character)))
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK002, "The SDK provenance source revision is missing or invalid.");
        }

        return value.ToLowerInvariant();
    }

    private static void ValidatePath(string? value)
    {
        var hasDrivePrefix = value is { Length: >= 2 } && char.IsLetter(value[0]) && value[1] == ':';
        if (string.IsNullOrWhiteSpace(value) || value.Contains('\\') || value.StartsWith('/') || hasDrivePrefix || Path.IsPathRooted(value) || value.Split('/').Any(static part => part is "" or "." or "..") || value.Any(static character => character < 32))
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK009, "The SDK provenance manifest contains an unsafe path.");
        }
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        NewLine = "\n",
    };

    private sealed record ManifestDocument(
        int SchemaVersion,
        string PackageId,
        string PackageVersion,
        string SourceRevision,
        string ManifestPath,
        string HashAlgorithm,
        string Self,
        ManifestEntry[] PackageEntries,
        ManifestEntry[] SourceEntries,
        string PackageContentDigest,
        string SourceContentDigest);

    private sealed record ManifestEntry(
        string Path,
        string PackagePath,
        long Length,
        string Sha256);
}
