namespace NetWasm.Compiler.Tasks.ComponentModel;

internal sealed record WitWorkerPackageMaterializationRequest(
    string AuthoredPath,
    string PlatformPackagePath,
    string OutputDirectory);

internal interface IWitWorkerPackageMaterializer
{
    string Materialize(WitWorkerPackageMaterializationRequest request);
}

internal sealed class WitWorkerPackageMaterializer : IWitWorkerPackageMaterializer
{
    private const string PlatformRelativePath = "deps/platform.wit.wasm";

    public string Materialize(WitWorkerPackageMaterializationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var authored = Path.GetFullPath(request.AuthoredPath);
        if (File.Exists(authored))
        {
            return authored;
        }
        if (!Directory.Exists(authored))
        {
            throw new DirectoryNotFoundException(
                "The authored WIT package directory does not exist.");
        }

        var platform = Path.GetFullPath(request.PlatformPackagePath);
        if (!File.Exists(platform))
        {
            throw new FileNotFoundException(
                "The pinned NetWasm platform WIT package does not exist.",
                platform);
        }
        var output = Path.GetFullPath(request.OutputDirectory);
        RejectOverlap(authored, output);

        var sources = Directory.GetFiles(authored, "*", SearchOption.AllDirectories)
            .Select(path => new SourceFile(
                Path.GetRelativePath(authored, path).Replace('\\', '/'),
                path))
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToArray();
        var platformSource = sources.SingleOrDefault(file => string.Equals(
            file.RelativePath,
            PlatformRelativePath,
            StringComparison.Ordinal));
        if (platformSource is not null && !FilesEqual(platformSource.Path, platform))
        {
            throw new InvalidDataException(
                "The authored WIT package defines a conflicting netwasm:platform dependency.");
        }
        SourceFile[] expected = platformSource is null
            ? [.. sources, new SourceFile(PlatformRelativePath, platform)]
            : sources;
        Array.Sort(expected, static (left, right) =>
            StringComparer.Ordinal.Compare(left.RelativePath, right.RelativePath));
        if (Matches(output, expected))
        {
            return output;
        }

        var parent = Path.GetDirectoryName(output)
            ?? throw new ArgumentException(
                "The resolved WIT package requires a parent directory.",
                nameof(request));
        Directory.CreateDirectory(parent);
        var temporary = Path.Combine(
            parent,
            $".{Path.GetFileName(output)}.{Guid.NewGuid():N}.tmp");
        try
        {
            foreach (var source in expected)
            {
                var destination = Path.Combine(
                    temporary,
                    source.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source.Path, destination);
                File.SetLastWriteTimeUtc(
                    destination,
                    File.GetLastWriteTimeUtc(source.Path));
            }
            if (Directory.Exists(output))
            {
                Directory.Delete(output, recursive: true);
            }
            Directory.Move(temporary, output);
            return output;
        }
        finally
        {
            if (Directory.Exists(temporary))
            {
                Directory.Delete(temporary, recursive: true);
            }
        }
    }

    private static bool Matches(string output, SourceFile[] expected)
    {
        if (!Directory.Exists(output))
        {
            return false;
        }
        var actual = Directory.GetFiles(output, "*", SearchOption.AllDirectories)
            .Select(path => new SourceFile(
                Path.GetRelativePath(output, path).Replace('\\', '/'),
                path))
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToArray();
        return actual.Length == expected.Length
            && actual.Zip(expected).All(pair =>
                string.Equals(
                    pair.First.RelativePath,
                    pair.Second.RelativePath,
                    StringComparison.Ordinal)
                && FilesEqual(pair.First.Path, pair.Second.Path));
    }

    private static bool FilesEqual(string left, string right)
    {
        var leftInfo = new FileInfo(left);
        var rightInfo = new FileInfo(right);
        return leftInfo.Length == rightInfo.Length
            && File.ReadAllBytes(left).AsSpan().SequenceEqual(File.ReadAllBytes(right));
    }

    private static void RejectOverlap(string source, string output)
    {
        var relativeOutput = Path.GetRelativePath(source, output);
        var relativeSource = Path.GetRelativePath(output, source);
        if (IsSelfOrChild(relativeOutput) || IsSelfOrChild(relativeSource))
        {
            throw new ArgumentException(
                "The authored and resolved WIT package directories must not overlap.");
        }
    }

    private static bool IsSelfOrChild(string relative) =>
        relative == "."
        || !relative.Equals("..", StringComparison.Ordinal)
            && !relative.StartsWith(
                string.Concat("..", Path.DirectorySeparatorChar),
                StringComparison.Ordinal)
            && !Path.IsPathFullyQualified(relative);

    private sealed record SourceFile(string RelativePath, string Path);
}
