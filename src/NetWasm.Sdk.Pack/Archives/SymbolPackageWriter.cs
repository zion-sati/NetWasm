using System.Collections.Immutable;
using System.Security.Cryptography;

namespace NetWasm.Sdk.Pack.Archives;

public sealed class SymbolPackageWriter : ISymbolPackageWriter
{
    private readonly INuspecWriter nuspecWriter;
    private readonly IPackageFileReader fileReader;
    private readonly IArchiveWriter archiveWriter;
    private readonly IPackagePathValidator pathValidator;

    public SymbolPackageWriter(
        INuspecWriter nuspecWriter,
        IPackageFileReader fileReader,
        IArchiveWriter archiveWriter,
        IPackagePathValidator pathValidator)
    {
        this.nuspecWriter = nuspecWriter ?? throw new ArgumentNullException(nameof(nuspecWriter));
        this.fileReader = fileReader ?? throw new ArgumentNullException(nameof(fileReader));
        this.archiveWriter = archiveWriter ?? throw new ArgumentNullException(nameof(archiveWriter));
        this.pathValidator = pathValidator ?? throw new ArgumentNullException(nameof(pathValidator));
    }

    public SymbolPackageOutput? Write(CanonicalPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (!package.Symbols.IncludeSymbols)
        {
            return null;
        }

        if (!string.Equals(package.Symbols.Format, "snupkg", StringComparison.OrdinalIgnoreCase))
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK012, "Only the supported snupkg symbol format is accepted.");
        }

        if (package.Symbols.Files.IsDefaultOrEmpty)
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK011, "Symbol output was requested but no symbol input was supplied.");
        }

        var outputPath = package.SymbolOutputPath;
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            outputPath = Path.ChangeExtension(package.OutputPath, ".snupkg");
        }

        var entries = new List<ArchiveEntry>
        {
            ArchiveEntry.FromBytes("[Content_Types].xml", Utf8("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\" /><Default Extension=\"nuspec\" ContentType=\"application/octet-stream\" /><Default Extension=\"pdb\" ContentType=\"application/octet-stream\" /></Types>")),
            ArchiveEntry.FromBytes("_rels/.rels", Utf8("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\" />")),
            ArchiveEntry.FromBytes($"{package.Identity.Id}.nuspec", nuspecWriter.Write(package with
            {
                SuppressDependencies = true,
                Files = []
            }))
        };
        if (package.Determinism.MaxEntries <= 0 || package.Determinism.MaxEntryBytes <= 0 || package.Determinism.MaxArchiveBytes <= 0)
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK013, "The deterministic archive size policy is invalid.");
        }

        var totalBytes = 0L;
        foreach (var entry in entries)
        {
            EnsureArchiveCapacity(totalBytes, entry.Content.Length, package.Determinism.MaxArchiveBytes);
            totalBytes += entry.Content.Length;
        }

        foreach (var file in package.Symbols.Files.OrderBy(static file => file.TargetPath, StringComparer.Ordinal))
        {
            var target = pathValidator.Validate(file.TargetPath);
            if (!target.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase) || entries.Any(entry => string.Equals(entry.Path, target, StringComparison.OrdinalIgnoreCase)))
            {
                throw new NetWasmPackException(NetWasmPackErrorCode.NWPK012, "A symbol input has an invalid or duplicate package path.");
            }

            var remainingBytes = package.Determinism.MaxArchiveBytes - totalBytes;
            if (remainingBytes <= 0)
            {
                throw new NetWasmPackException(NetWasmPackErrorCode.NWPK014, "The symbol archive exceeds the deterministic size policy.");
            }

            byte[] bytes;
            try
            {
                bytes = fileReader.Read(file.SourcePath, Math.Min(package.Determinism.MaxEntryBytes, remainingBytes));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FileNotFoundException)
            {
                throw new NetWasmPackException(NetWasmPackErrorCode.NWPK011, "A symbol input could not be read.");
            }

            if (file.ExpectedLength is not null && file.ExpectedLength.Value != bytes.LongLength ||
                !string.IsNullOrWhiteSpace(file.ExpectedSha256) &&
                !string.Equals(file.ExpectedSha256, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), StringComparison.OrdinalIgnoreCase))
            {
                throw new NetWasmPackException(NetWasmPackErrorCode.NWPK010, "A symbol input hash does not match restore output.");
            }

            EnsureArchiveCapacity(totalBytes, bytes.Length, package.Determinism.MaxArchiveBytes);
            totalBytes += bytes.Length;
            entries.Add(ArchiveEntry.FromBytes(target, bytes, file.SourcePath));
        }

        var plan = new ArchivePlan(
            outputPath,
            entries.OrderBy(static entry => entry.Path, StringComparer.Ordinal).ToImmutableArray(),
            package.Determinism,
            package.Identity);
        var output = archiveWriter.Write(plan);
        return new SymbolPackageOutput(output.Path, output.Sha256, output.Identity);
    }

    private static byte[] Utf8(string value) => System.Text.Encoding.UTF8.GetBytes(value);

    private static void EnsureArchiveCapacity(long currentBytes, long addedBytes, long maxArchiveBytes)
    {
        if (addedBytes > maxArchiveBytes - currentBytes)
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK014, "The symbol archive exceeds the deterministic size policy.");
        }
    }
}
