namespace NetWasm.Hosting.Build.Deployment;

public interface IBuildArtifactStore
{
    byte[] Read(string path);
    void Write(string path, ReadOnlySpan<byte> content);
    void Delete(string path);
}

public sealed class BuildArtifactStore : IBuildArtifactStore
{
    public byte[] Read(string path)
    {
        ValidateAbsolutePath(path);
        return File.ReadAllBytes(path);
    }

    public void Write(string path, ReadOnlySpan<byte> content)
    {
        ValidateAbsolutePath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
    }

    public void Delete(string path)
    {
        ValidateAbsolutePath(path);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void ValidateAbsolutePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path) || path.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException("Build artifact paths must be absolute.", nameof(path));
        }
    }
}
