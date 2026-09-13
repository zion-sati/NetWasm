using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NetWasm.Toolchain.Manifest;

namespace NetWasm.Toolchain.Resolution;

internal static class JavaScriptClosureIntegrityVerifier
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static void Verify(
        string packageRoot,
        string manifestRelativePath,
        bool allowBundleCommand)
    {
        if (!Path.IsPathFullyQualified(packageRoot))
        {
            throw new ArgumentException(
                "The package root must be an absolute path.",
                nameof(packageRoot));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(manifestRelativePath);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(packageRoot));
        var manifestPath = ResolveContainedPath(root, manifestRelativePath);
        if (!File.Exists(manifestPath))
        {
            throw new ToolchainAssetMissingException(manifestPath);
        }

        JcoClosureIntegrityManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<JcoClosureIntegrityManifest>(
                File.ReadAllText(manifestPath),
                SerializerOptions)
                ?? throw new InvalidDataException(
                    "The JavaScript closure integrity manifest is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The JavaScript closure integrity manifest is invalid.",
                exception);
        }

        if (!string.Equals(manifest.SchemaVersion, "1", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The JavaScript closure integrity manifest schema is unsupported.");
        }

        if (manifest.Files.IsDefaultOrEmpty)
        {
            throw new InvalidDataException(
                "The JavaScript closure integrity manifest contains no files.");
        }

        var closureRoot = Path.GetDirectoryName(manifestPath)!;
        RequirePhysicalContainment(root, manifestPath);
        var paths = new HashSet<string>(StringComparer.Ordinal);
        var portablePaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in manifest.Files)
        {
            ArgumentNullException.ThrowIfNull(entry);
            ArgumentException.ThrowIfNullOrWhiteSpace(entry.Path);
            ArgumentException.ThrowIfNullOrWhiteSpace(entry.Sha256);
            var relativePath = NormalizeManifestPath(entry.Path);
            if (!string.Equals(relativePath, entry.Path, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "The JavaScript closure integrity manifest must use '/' path separators.");
            }
            if (!paths.Add(relativePath))
            {
                throw new InvalidDataException(
                    $"The JavaScript closure integrity manifest contains duplicate path '{relativePath}'.");
            }

            if (!portablePaths.Add(GetPortablePathKey(relativePath)))
            {
                throw new InvalidDataException(
                    $"The JavaScript closure integrity manifest contains a case or Unicode alias for '{relativePath}'.");
            }

            var assetPath = ResolveContainedPath(closureRoot, relativePath);
            RequirePhysicalContainment(closureRoot, assetPath);
            if (!IsRegularFile(assetPath))
            {
                throw new ToolchainAssetMissingException(assetPath);
            }

            var actualSha256 = Convert.ToHexString(
                SHA256.HashData(File.ReadAllBytes(assetPath)));
            if (!string.Equals(actualSha256, entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"SHA-256 digest mismatch for '{assetPath}'.");
            }
        }

        var actualPaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in EnumerateRegularFiles(closureRoot))
        {
            if (string.Equals(
                    Path.GetFullPath(path),
                    Path.GetFullPath(manifestPath),
                    StringComparison.Ordinal))
            {
                continue;
            }

            var relativePath = NormalizeManifestPath(Path.GetRelativePath(closureRoot, path));
            if (IsControlFile(relativePath, allowBundleCommand))
            {
                continue;
            }

            actualPaths.Add(relativePath);
        }

        if (!paths.SetEquals(actualPaths))
        {
            throw new InvalidDataException(
                "The JavaScript closure integrity manifest does not describe the exact closure inventory.");
        }
    }

    private static string ResolveContainedPath(string root, string relativePath)
    {
        if (Path.IsPathFullyQualified(relativePath))
        {
            throw new InvalidDataException(
                "The JavaScript closure integrity manifest contains an absolute path.");
        }

        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        var relativeToRoot = Path.GetRelativePath(root, fullPath);
        if (string.Equals(relativeToRoot, ".", StringComparison.Ordinal)
            || string.Equals(relativeToRoot, "..", StringComparison.Ordinal)
            || relativeToRoot.StartsWith($"..{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal)
            || relativeToRoot.StartsWith($"..{Path.AltDirectorySeparatorChar}",
                StringComparison.Ordinal)
            || Path.IsPathFullyQualified(relativeToRoot))
        {
            throw new InvalidDataException(
                "The JavaScript closure integrity manifest contains an unsafe path.");
        }

        return fullPath;
    }

    private static string NormalizeManifestPath(string path)
    {
        var normalized = path.Replace('\\', '/').Replace(Path.DirectorySeparatorChar, '/');
        var parts = normalized.Split('/');
        if (parts.Any(part => string.IsNullOrEmpty(part) || part is "." or ".."))
        {
            throw new InvalidDataException(
                "The JavaScript closure integrity manifest contains an unsafe path.");
        }

        foreach (var part in parts)
        {
            if (part.Any(character => character < 32
                    || character == 127
                    || "<>:\"|?*".Contains(character))
                || part.Contains(';')
                || part.Contains("$(", StringComparison.Ordinal)
                || part.Contains("@(", StringComparison.Ordinal)
                || part.Contains("%(", StringComparison.Ordinal)
                || part.EndsWith('.')
                || part.EndsWith(' '))
            {
                throw new InvalidDataException(
                    "The JavaScript closure integrity manifest contains a non-portable path.");
            }

            var stem = part.Split('.', 2)[0];
            if (stem.Equals("con", StringComparison.OrdinalIgnoreCase)
                || stem.Equals("prn", StringComparison.OrdinalIgnoreCase)
                || stem.Equals("aux", StringComparison.OrdinalIgnoreCase)
                || stem.Equals("nul", StringComparison.OrdinalIgnoreCase)
                || (stem.Length == 4
                    && (stem.StartsWith("com", StringComparison.OrdinalIgnoreCase)
                        || stem.StartsWith("lpt", StringComparison.OrdinalIgnoreCase))
                    && char.IsDigit(stem[3])))
            {
                throw new InvalidDataException(
                    "The JavaScript closure integrity manifest contains a reserved path.");
            }
        }

        return normalized;
    }

    private static string GetPortablePathKey(string path)
    {
        // Windows and the default macOS volume comparison are case-insensitive;
        // Unicode normalization also prevents an NFC/NFD pair from becoming two
        // files in a Linux-built package that cannot be extracted portably.
        return path.Normalize(NormalizationForm.FormKC).ToUpperInvariant();
    }

    private static bool IsRegularFile(string path)
    {
        return File.Exists(path)
            && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0;
    }

    private static bool IsControlFile(string relativePath, bool allowBundleCommand)
    {
        return string.Equals(relativePath, "package-lock.json", StringComparison.Ordinal)
            || string.Equals(relativePath, "notices.json", StringComparison.Ordinal)
            || string.Equals(relativePath, "closure-pack-items.props", StringComparison.Ordinal)
            || string.Equals(relativePath, "closure-policy.json", StringComparison.Ordinal)
            || (allowBundleCommand
                && string.Equals(
                    relativePath,
                    "netwasm-bundle.mjs",
                    StringComparison.Ordinal));
    }

    private static IEnumerable<string> EnumerateRegularFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            foreach (var directory in Directory.EnumerateDirectories(current))
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException(
                        $"The JavaScript closure contains a symbolic or reparse directory '{directory}'.");
                }

                pending.Push(directory);
            }

            foreach (var file in Directory.EnumerateFiles(current))
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException(
                        $"The JavaScript closure contains a symbolic or reparse file '{file}'.");
                }

                yield return file;
            }
        }
    }

    private static void RequirePhysicalContainment(string root, string candidate)
    {
        var physicalRoot = ResolvePhysicalPath(root);
        var physicalCandidate = ResolvePhysicalPath(candidate);
        var relative = Path.GetRelativePath(physicalRoot, physicalCandidate);
        if (string.Equals(relative, ".", StringComparison.Ordinal)
            || string.Equals(relative, "..", StringComparison.Ordinal)
            || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal)
            || Path.IsPathFullyQualified(relative))
        {
            throw new InvalidDataException(
                "The JavaScript closure path resolves outside its package root.");
        }
    }

    private static string ResolvePhysicalPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath)!;
        var current = root;
        var remainder = fullPath[root.Length..];
        var parts = remainder.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            current = Path.Combine(current, part);
            FileSystemInfo link = Directory.Exists(current)
                ? new DirectoryInfo(current)
                : new FileInfo(current);
            var target = link.Exists
                ? link.ResolveLinkTarget(returnFinalTarget: true)
                : null;
            if (target is not null)
            {
                current = target.FullName;
            }
        }

        return Path.GetFullPath(current);
    }
}
