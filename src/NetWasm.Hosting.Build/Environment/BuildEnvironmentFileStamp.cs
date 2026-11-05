namespace NetWasm.Hosting.Build.Environment;

/// <summary>A file's size and last write time, or null when it does not exist.</summary>
public static class BuildEnvironmentFileStamp
{
    public static string? Of(string path) => File.Exists(path) ? Of(new FileInfo(path)) : null;

    public static string Of(FileInfo file) =>
        $"{file.Length}:{file.LastWriteTimeUtc.Ticks}";
}
