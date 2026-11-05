using System.Text.Json;

namespace NetWasm.Hosting.Build.Environment;

/// <summary>Reads the outputs a build resolved under the same stamp, if their files are unchanged.</summary>
public interface IBuildEnvironmentCacheReader
{
    IReadOnlyDictionary<string, string>? Read(string path, string stamp);
}

public sealed class BuildEnvironmentCacheReader : IBuildEnvironmentCacheReader
{
    public IReadOnlyDictionary<string, string>? Read(string path, string stamp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(stamp);
        BuildEnvironmentCacheEntry? entry;
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }
            entry = JsonSerializer.Deserialize(File.ReadAllBytes(path),
                HostingBuildJsonContext.Default.BuildEnvironmentCacheEntry);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
        if (entry?.Outputs is null || entry.Files is null || entry.Stamp != stamp)
        {
            return null;
        }
        // Tools outside the packages, such as a node from PATH, are named by path only.
        return entry.Files.All(file => BuildEnvironmentFileStamp.Of(file.Key) == file.Value)
            ? entry.Outputs
            : null;
    }
}
