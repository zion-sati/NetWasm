using System.Text.Json;

namespace NetWasm.Hosting.Build.Environment;

/// <summary>Records resolved outputs under their stamp, with the files they name.</summary>
public interface IBuildEnvironmentCacheWriter
{
    void Write(string path, string stamp, IReadOnlyDictionary<string, string> outputs);
}

public sealed class BuildEnvironmentCacheWriter : IBuildEnvironmentCacheWriter
{
    public void Write(string path, string stamp, IReadOnlyDictionary<string, string> outputs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(stamp);
        ArgumentNullException.ThrowIfNull(outputs);
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var value in outputs.Values)
        {
            if (Path.IsPathFullyQualified(value) && BuildEnvironmentFileStamp.Of(value) is { } fileStamp)
            {
                files[value] = fileStamp;
            }
        }
        var entry = new BuildEnvironmentCacheEntry(
            stamp, new Dictionary<string, string>(outputs, StringComparer.Ordinal), files);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllBytes(temporary,
                JsonSerializer.SerializeToUtf8Bytes(entry, HostingBuildJsonContext.Default.BuildEnvironmentCacheEntry));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The cache only saves time; the next build resolves again.
        }
        finally
        {
            try { File.Delete(temporary); } catch (IOException) { }
        }
    }
}
