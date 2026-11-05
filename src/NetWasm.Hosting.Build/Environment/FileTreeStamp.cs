using System.Security.Cryptography;
using System.Text;

namespace NetWasm.Hosting.Build.Environment;

/// <summary>
/// A digest of values and of the size and write time of files: each named file, and
/// every file under each named directory. Equal stamps mean the same inputs as far as
/// incremental work can tell without reading them, which is what MSBuild relies on too.
/// </summary>
public static class FileTreeStamp
{
    private static readonly EnumerationOptions AllFiles = new()
    {
        RecurseSubdirectories = true,
        AttributesToSkip = 0,
        IgnoreInaccessible = false,
    };

    public static string Compute(IEnumerable<string?> values, IEnumerable<string?> paths)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(paths);
        var text = new StringBuilder();
        foreach (var value in values)
        {
            Append(text, "value", value);
        }
        foreach (var path in paths)
        {
            AppendPath(text, path);
        }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    internal static void AppendPath(StringBuilder text, string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            Append(text, path, BuildEnvironmentFileStamp.Of(new FileInfo(path)));
            return;
        }
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            Append(text, path ?? string.Empty, null);
            return;
        }
        Append(text, path, "directory");
        var files = new DirectoryInfo(path).EnumerateFiles("*", AllFiles)
            .Select(file => (Path: Path.GetRelativePath(path, file.FullName), File: file))
            .OrderBy(file => file.Path, StringComparer.Ordinal);
        foreach (var (relative, file) in files)
        {
            Append(text, relative, BuildEnvironmentFileStamp.Of(file));
        }
    }

    internal static void Append(StringBuilder text, string name, string? value) =>
        text.Append(name).Append('\0').Append(value ?? "\u0001").Append('\n');
}
