namespace NetWasm.TimeZones;

internal sealed class ArtifactWriter : IArtifactWriter
{
    public void Write(string path, byte[] contents)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(contents);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(fullPath, contents);
    }
}
