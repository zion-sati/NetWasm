using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace NetWasm.Sdk.Pack.Archives;

public sealed class DeterministicArchiveWriter : IArchiveWriter
{
    private readonly IPackageValidator validator;
    private readonly IArchiveOutputValidator outputValidator;

    public DeterministicArchiveWriter(IPackageValidator validator, IArchiveOutputValidator outputValidator)
    {
        this.validator = validator ?? throw new ArgumentNullException(nameof(validator));
        this.outputValidator = outputValidator ?? throw new ArgumentNullException(nameof(outputValidator));
    }

    public PackageOutput Write(ArchivePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        validator.Validate(plan);

        var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(plan.OutputPath))!;
        try
        {
            Directory.CreateDirectory(outputDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK014, "The package output directory could not be created.");
        }

        var outputPath = Path.GetFullPath(plan.OutputPath);
        var temporaryPath = Path.Combine(outputDirectory, $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            WriteArchive(temporaryPath, plan);
            if (new FileInfo(temporaryPath).Length > plan.Policy.MaxArchiveBytes)
            {
                throw new NetWasmPackException(NetWasmPackErrorCode.NWPK014, "The package archive exceeds the deterministic size policy.");
            }

            outputValidator.Validate(temporaryPath, plan);
            File.Move(temporaryPath, outputPath, overwrite: true);
            var hash = ComputeHash(outputPath);
            return new PackageOutput(outputPath, hash, plan.Identity);
        }
        catch (NetWasmPackException)
        {
            DeleteTemporaryFile(temporaryPath);
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            DeleteTemporaryFile(temporaryPath);
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK014, "The package archive could not be written.");
        }
        finally
        {
            DeleteTemporaryFile(temporaryPath);
        }
    }

    private static void WriteArchive(string path, ArchivePlan plan)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false, Encoding.UTF8);
        foreach (var item in plan.Entries.OrderBy(static entry => entry.Path, StringComparer.Ordinal))
        {
            var entry = archive.CreateEntry(item.Path, GetCompression(plan.Policy.Compression));
            entry.LastWriteTime = plan.Policy.EntryTimestamp;
            using var destination = entry.Open();
            destination.Write(item.Content.AsSpan());
        }
    }

    private static CompressionLevel GetCompression(Packing.CompressionMode compression) => compression switch
    {
        Packing.CompressionMode.Store => CompressionLevel.NoCompression,
        Packing.CompressionMode.Fastest => CompressionLevel.Fastest,
        Packing.CompressionMode.Optimal => CompressionLevel.Optimal,
        _ => throw new NetWasmPackException(NetWasmPackErrorCode.NWPK013, "The archive compression policy is unsupported.")
    };

    private static string ComputeHash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void DeleteTemporaryFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
