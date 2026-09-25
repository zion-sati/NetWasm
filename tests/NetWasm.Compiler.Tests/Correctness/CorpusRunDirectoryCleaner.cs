namespace NetWasm.Compiler.Tests.Correctness;

internal interface ICorpusRunDirectoryCleaner
{
    // The caller must own this freshly allocated root and have finished using it.
    void Clean(string directory);
}

internal sealed class CorpusRunDirectoryCleaner : ICorpusRunDirectoryCleaner
{
    public void Clean(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var name = Path.GetFileName(directory);
        const string prefix = "netwasm-corpus-";
        if (!Path.IsPathFullyQualified(directory) ||
            !string.Equals(Path.GetDirectoryName(directory), Path.TrimEndingDirectorySeparator(Path.GetTempPath()), StringComparison.Ordinal) ||
            !name.StartsWith(prefix, StringComparison.Ordinal) || name.Length == prefix.Length)
        {
            throw new ArgumentException("Cleanup requires an owned corpus root directly beneath the temporary directory.", nameof(directory));
        }
        var root = new DirectoryInfo(directory);
        if (!root.Exists)
        {
            throw new DirectoryNotFoundException("The owned corpus root no longer exists: " + directory);
        }
        if ((root.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("Cleanup cannot accept a linked corpus root: " + directory);
        }
        root.Delete(recursive: true);
    }
}
