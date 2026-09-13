namespace NetWasm.Sdk.Pack.Packing;

public sealed class AtomicPackArtifactWriter : IPackArtifactWriter
{
    public void Write(PackArtifacts artifacts)
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        var targets = new List<(string Path, byte[] Bytes)>();
        AddTarget(targets, artifacts.NuspecPath, artifacts.NuspecBytes);
        AddTarget(targets, artifacts.ManifestPath, artifacts.ManifestBytes);
        if (targets.Count == 0)
        {
            return;
        }

        if (targets.Select(static target => target.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != targets.Count)
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK014, "Package artifacts cannot share an output path.");
        }

        var staged = new List<(string Temporary, string Destination)>();
        var published = new List<string>();
        try
        {
            foreach (var target in targets)
            {
                var directory = Path.GetDirectoryName(target.Path)!;
                Directory.CreateDirectory(directory);
                var temporary = Path.Combine(directory, $".{Path.GetFileName(target.Path)}.{Guid.NewGuid():N}.tmp");
                File.WriteAllBytes(temporary, target.Bytes);
                staged.Add((temporary, target.Path));
            }

            foreach (var item in staged)
            {
                File.Move(item.Temporary, item.Destination, overwrite: true);
                published.Add(item.Destination);
            }
        }
        catch (Exception)
        {
            DeleteStaged(staged);
            DeletePublished(published);
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK014, "A package artifact could not be persisted.");
        }
    }

    private static void AddTarget(List<(string Path, byte[] Bytes)> targets, string? path, byte[] bytes)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(bytes);
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK014, "The package artifact path is invalid.");
        }
        targets.Add((fullPath, bytes));
    }

    private static void DeleteStaged(IEnumerable<(string Temporary, string Destination)> staged)
    {
        foreach (var item in staged)
        {
            if (File.Exists(item.Temporary))
            {
                File.Delete(item.Temporary);
            }
        }
    }

    private static void DeletePublished(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
