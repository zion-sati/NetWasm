using System.Collections.Immutable;
using System.IO.Compression;

using NetWasm.Sdk.Pack.Policies;

namespace NetWasm.Sdk.Pack.Archives;

public sealed class PackageArchiveReader : IPackageArchiveReader
{
    private readonly IPackagePathValidator pathValidator;

    public PackageArchiveReader(IPackagePathValidator pathValidator)
    {
        this.pathValidator = pathValidator ?? throw new ArgumentNullException(nameof(pathValidator));
    }

    public PackageArchive Read(PackageArchiveNormalizationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.InputPath))
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK011, "The package archive input path is missing.");
        }

        if (request.Policy is null || request.Identity is null)
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK014, "The package archive request is incomplete.");
        }

        if (request.Policy.MaxEntries <= 0 || request.Policy.MaxEntryBytes <= 0 || request.Policy.MaxArchiveBytes <= 0)
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK013, "The deterministic archive size policy is invalid.");
        }

        try
        {
            using var archive = ZipFile.OpenRead(request.InputPath);
            if (archive.Entries.Count == 0 || archive.Entries.Count > request.Policy.MaxEntries)
            {
                throw new NetWasmPackException(NetWasmPackErrorCode.NWPK014, "The package archive entry count is outside the deterministic policy.");
            }

            // A package signature covers the complete ZIP graph.  Rewriting
            // any entry would invalidate it, so reject signed input before
            // opening or buffering a single entry.  Signing is a separate,
            // final release operation owned by the signing pipeline.
            if (archive.Entries.Any(static entry => IsSignaturePath(entry.FullName)))
            {
                throw new NetWasmPackException(NetWasmPackErrorCode.NWPK014, "Signed package archives cannot be canonicalized; sign the completed archive as a final operation.");
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var entries = ImmutableArray.CreateBuilder<ArchiveEntry>(archive.Entries.Count);
            var totalBytes = 0L;
            foreach (var entry in archive.Entries)
            {
                var path = pathValidator.Validate(entry.FullName);
                if (!seen.Add(path))
                {
                    throw new NetWasmPackException(NetWasmPackErrorCode.NWPK008, "The package archive contains a duplicate path.");
                }

                var remainingBytes = request.Policy.MaxArchiveBytes - totalBytes;
                if (remainingBytes < 0 || entry.Length > Math.Min(request.Policy.MaxEntryBytes, remainingBytes))
                {
                    throw new NetWasmPackException(NetWasmPackErrorCode.NWPK014, "The package archive exceeds the deterministic size policy.");
                }

                using var stream = entry.Open();
                var content = ReadBounded(stream, Math.Min(request.Policy.MaxEntryBytes, remainingBytes));
                totalBytes += content.Length;
                entries.Add(ArchiveEntry.FromBytes(path, content, request.InputPath));
            }

            return new PackageArchive(request.Identity, entries.ToImmutable(), request.Policy);
        }
        catch (NetWasmPackException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK011, "The package archive could not be read.");
        }
    }

    private static bool IsSignaturePath(string path) =>
        path.Equals(".signature.p7s", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith("/.signature.p7s", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("package/services/metadata/signatures/", StringComparison.OrdinalIgnoreCase) &&
        path.EndsWith(".p7s", StringComparison.OrdinalIgnoreCase);

    private static byte[] ReadBounded(Stream stream, long maxLength)
    {
        if (maxLength < 0)
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK013, "The deterministic archive entry-size policy is invalid.");
        }

        using var memory = new MemoryStream();
        var buffer = new byte[81920];
        var total = 0L;
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) != 0)
        {
            if (read > maxLength - total)
            {
                throw new NetWasmPackException(NetWasmPackErrorCode.NWPK014, "A package archive entry exceeds the deterministic size policy.");
            }

            total += read;
            memory.Write(buffer, 0, read);
        }

        return memory.ToArray();
    }
}
