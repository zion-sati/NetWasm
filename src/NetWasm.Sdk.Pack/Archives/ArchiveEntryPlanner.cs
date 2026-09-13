using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace NetWasm.Sdk.Pack.Archives;

public sealed class ArchiveEntryPlanner : IArchivePlanner
{
    private readonly INuspecWriter nuspecWriter;
    private readonly IPackageFileReader fileReader;
    private readonly IPackagePathValidator pathValidator;

    public ArchiveEntryPlanner(INuspecWriter nuspecWriter, IPackageFileReader fileReader, IPackagePathValidator pathValidator)
    {
        this.nuspecWriter = nuspecWriter ?? throw new ArgumentNullException(nameof(nuspecWriter));
        this.fileReader = fileReader ?? throw new ArgumentNullException(nameof(fileReader));
        this.pathValidator = pathValidator ?? throw new ArgumentNullException(nameof(pathValidator));
    }

    public ArchivePlan Plan(CanonicalPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (package.Determinism.MaxEntries <= 0 || package.Determinism.MaxEntryBytes <= 0 || package.Determinism.MaxArchiveBytes <= 0)
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK013, "The deterministic archive size policy is invalid.");
        }

        var entries = new List<ArchiveEntry>
        {
            ArchiveEntry.FromBytes("[Content_Types].xml", ContentTypes),
            ArchiveEntry.FromBytes("_rels/.rels", Relationships),
            ArchiveEntry.FromBytes($"{package.Identity.Id}.nuspec", nuspecWriter.Write(package))
        };
        var totalBytes = 0L;
        foreach (var entry in entries)
        {
            EnsureArchiveCapacity(totalBytes, entry.Content.Length, package.Determinism.MaxArchiveBytes);
            totalBytes += entry.Content.Length;
        }

        foreach (var file in package.Files.OrderBy(static file => file.TargetPath, StringComparer.Ordinal))
        {
            var path = pathValidator.Validate(file.TargetPath);
            if (entries.Any(entry => string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                throw new NetWasmPackException(NetWasmPackErrorCode.NWPK008, "A package archive path occurs more than once.");
            }

            var remainingBytes = package.Determinism.MaxArchiveBytes - totalBytes;
            if (remainingBytes <= 0)
            {
                throw new NetWasmPackException(NetWasmPackErrorCode.NWPK014, "The package archive exceeds the deterministic size policy.");
            }

            byte[] content;
            try
            {
                content = fileReader.Read(file.SourcePath, Math.Min(package.Determinism.MaxEntryBytes, remainingBytes));
            }
            catch (NetWasmPackException)
            {
                throw;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FileNotFoundException)
            {
                throw new NetWasmPackException(NetWasmPackErrorCode.NWPK011, "A package input file could not be read.");
            }

            ArgumentNullException.ThrowIfNull(content);
            ValidateExpectedInput(file.ExpectedLength, file.ExpectedSha256, content);
            EnsureArchiveCapacity(totalBytes, content.Length, package.Determinism.MaxArchiveBytes);
            totalBytes += content.Length;
            entries.Add(ArchiveEntry.FromBytes(path, content, file.SourcePath));
        }

        var ordered = entries.OrderBy(static entry => entry.Path, StringComparer.Ordinal).ToImmutableArray();
        return new ArchivePlan(package.OutputPath, ordered, package.Determinism, package.Identity);
    }

    private static readonly byte[] ContentTypes = Utf8(
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
        "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
        "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\" />" +
        "<Default Extension=\"nuspec\" ContentType=\"application/octet-stream\" />" +
        "<Default Extension=\"dll\" ContentType=\"application/octet-stream\" />" +
        "<Default Extension=\"pdb\" ContentType=\"application/octet-stream\" />" +
        "<Default Extension=\"xml\" ContentType=\"application/octet-stream\" />" +
        "</Types>");

    private static readonly byte[] Relationships = Utf8(
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\" />");

    private static byte[] Utf8(string value) => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(value);

    private static void EnsureArchiveCapacity(long currentBytes, long addedBytes, long maxArchiveBytes)
    {
        if (addedBytes > maxArchiveBytes - currentBytes)
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK014, "The package archive exceeds the deterministic size policy.");
        }
    }

    private static void ValidateExpectedInput(long? expectedLength, string? expectedSha256, byte[] content)
    {
        if (expectedLength is not null && expectedLength.Value != content.LongLength)
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK010, "A package input changed after it was evaluated.");
        }

        if (!string.IsNullOrWhiteSpace(expectedSha256) &&
            !string.Equals(expectedSha256, Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(), StringComparison.OrdinalIgnoreCase))
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK010, "A package input hash does not match restore output.");
        }
    }
}
