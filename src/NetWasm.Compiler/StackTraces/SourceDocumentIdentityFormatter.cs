using System;
using System.IO;
using System.Linq;

namespace NetWasm.Compiler.StackTraces;

internal sealed class SourceDocumentIdentityFormatter :
    ISourceDocumentIdentityFormatter
{
    public string Format(string document, CompilerOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(document);
        ArgumentNullException.ThrowIfNull(options);

        var normalized = document.Replace('\\', '/');
        if (Uri.TryCreate(normalized, UriKind.Absolute, out var uri) &&
            uri.Scheme is "http" or "https")
        {
            return normalized;
        }
        if (normalized.StartsWith("/_/", StringComparison.Ordinal))
        {
            return normalized;
        }
        foreach (var destination in PathMapDestinations(options.PathMap))
        {
            if (IsAtOrUnder(normalized, destination))
            {
                return normalized;
            }
        }
        if (!IsRooted(normalized))
        {
            return normalized;
        }
        if (IsForeignWindowsPath(normalized, OperatingSystem.IsWindows()))
        {
            return FileName(normalized);
        }
        if (!string.IsNullOrWhiteSpace(options.ProjectDirectory))
        {
            var project = Path.GetFullPath(options.ProjectDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullDocument = Path.GetFullPath(document);
            var relative = Path.GetRelativePath(project, fullDocument)
                .Replace('\\', '/');
            if (relative != ".." &&
                !relative.StartsWith("../", StringComparison.Ordinal))
            {
                return relative;
            }
        }
        return FileName(normalized);
    }

    private static bool IsRooted(string path) =>
        Path.IsPathRooted(path) ||
        (path.Length >= 3 && char.IsAsciiLetter(path[0]) &&
         path[1] == ':' && path[2] == '/') ||
        path.StartsWith("//", StringComparison.Ordinal);

    internal static bool IsForeignWindowsPath(string path, bool isWindows) =>
        !isWindows &&
        ((path.Length >= 3 && char.IsAsciiLetter(path[0]) &&
          path[1] == ':' && path[2] == '/') ||
         path.StartsWith("//", StringComparison.Ordinal));

    private static bool IsAtOrUnder(string path, string root)
    {
        root = root.TrimEnd('/');
        return root.Length != 0 &&
            (string.Equals(path, root, StringComparison.Ordinal) ||
             path.StartsWith(root + "/", StringComparison.Ordinal));
    }

    private static string[] PathMapDestinations(string? pathMap)
    {
        if (string.IsNullOrWhiteSpace(pathMap))
        {
            return [];
        }
        return
        [
            .. pathMap.Split(',', StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries)
                .Select(static mapping =>
                {
                    var separator = mapping.IndexOf('=');
                    return separator < 0 ? string.Empty :
                        mapping[(separator + 1)..].Replace('\\', '/');
                })
                .Where(static destination => destination.Length != 0),
        ];
    }

    private static string FileName(string path) =>
        path[(path.LastIndexOf('/') + 1)..];
}
