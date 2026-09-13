using System.IO.Compression;

namespace NetWasm.Sdk.Pack.Archives;

public sealed class PackageArchiveOutputValidator : IArchiveOutputValidator
{
    private readonly IPackagePathValidator pathValidator;

    public PackageArchiveOutputValidator(IPackagePathValidator pathValidator)
    {
        this.pathValidator = pathValidator ?? throw new ArgumentNullException(nameof(pathValidator));
    }

    public void Validate(string archivePath, ArchivePlan plan)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Policy is null || plan.Policy.MaxEntries <= 0 || plan.Policy.MaxEntryBytes <= 0 || plan.Policy.MaxArchiveBytes <= 0)
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK013, "The deterministic archive size policy is invalid.");
        }

        try
        {
            using var archive = ZipFile.OpenRead(archivePath);
            var entries = archive.Entries.OrderBy(static entry => entry.FullName, StringComparer.Ordinal).ToArray();
            if (entries.Length != plan.Entries.Length)
            {
                throw new NetWasmPackException(NetWasmPackErrorCode.NWPK014, "The written package archive has an unexpected entry count.");
            }

            var expectedEntries = plan.Entries
                .OrderBy(static entry => entry.Path, StringComparer.Ordinal)
                .ToArray();
            var totalBytes = 0L;
            for (var index = 0; index < entries.Length; index++)
            {
                var expected = expectedEntries[index];
                var actual = entries[index];
                if (!string.Equals(pathValidator.Validate(actual.FullName), expected.Path, StringComparison.Ordinal))
                {
                    throw new NetWasmPackException(NetWasmPackErrorCode.NWPK014, "The written package archive has an unexpected entry path.");
                }

                var remainingBytes = plan.Policy.MaxArchiveBytes - totalBytes;
                if (remainingBytes < 0 || actual.Length > Math.Min(plan.Policy.MaxEntryBytes, remainingBytes))
                {
                    throw new NetWasmPackException(NetWasmPackErrorCode.NWPK014, "The written package archive exceeds the deterministic size policy.");
                }

                using var stream = actual.Open();
                var content = ReadBounded(stream, Math.Min(plan.Policy.MaxEntryBytes, remainingBytes));
                totalBytes += content.Length;
                if (!content.AsSpan().SequenceEqual(expected.Content.AsSpan()))
                {
                    throw new NetWasmPackException(NetWasmPackErrorCode.NWPK014, "The written package archive changed an entry during serialization.");
                }
            }
        }
        catch (NetWasmPackException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK014, "The written package archive could not be reopened for validation.");
        }
    }

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
            total += read;
            if (total > maxLength)
            {
                throw new NetWasmPackException(NetWasmPackErrorCode.NWPK014, "The written package archive entry exceeds the deterministic size policy.");
            }

            memory.Write(buffer, 0, read);
        }

        return memory.ToArray();
    }
}
