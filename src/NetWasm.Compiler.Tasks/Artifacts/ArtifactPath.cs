namespace NetWasm.Compiler.Tasks.Artifacts;

// A path as the file system resolves it. MSBuild and the operating system can name one file
// through a symbolic link or by its target (macOS's /var and /tmp link to /private/var and
// /private/tmp), so artifact paths are compared and made relative only in this form.
internal static class ArtifactPath
{
    // Links to follow before giving up on a cycle, as the operating system does.
    private const int MaximumLinks = 40;

    public static string Resolve(string path, string? baseDirectory = null) =>
        Resolve(baseDirectory is null ? Path.GetFullPath(path) : Path.GetFullPath(path, baseDirectory), MaximumLinks);

    private static string Resolve(string full, int links)
    {
        var root = Path.GetPathRoot(full) ?? string.Empty;
        var resolved = root;
        foreach (var part in full[root.Length..].Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            resolved = Path.Combine(resolved, part);
            FileSystemInfo entry = Directory.Exists(resolved) ? new DirectoryInfo(resolved) : new FileInfo(resolved);
            // A link's target may itself go through links, so it is resolved in turn.
            if (links > 0 && entry.LinkTarget is { } target)
            {
                resolved = Resolve(Path.GetFullPath(target, Path.GetDirectoryName(resolved) ?? root), links - 1);
            }
        }

        return resolved;
    }
}
