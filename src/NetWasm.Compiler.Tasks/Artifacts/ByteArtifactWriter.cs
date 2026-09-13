namespace NetWasm.Compiler.Tasks.Artifacts;

internal sealed class ByteArtifactWriter : IByteArtifactWriter
{
    public void Write(string path, byte[] bytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(bytes);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllBytes(path, bytes);
    }
}
