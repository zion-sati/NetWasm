using System.Text.Json;

namespace NetWasm.Hosting.Build.Execution;

/// <summary>
/// A file of a browser project's site: its source, and the path it is served at, relative to the
/// publish directory. A fallback file is served only where no other file is.
/// </summary>
public sealed record SiteFile(string SourcePath, string SitePath, bool Fallback = false);

/// <summary>A file publish copies, at its path relative to the publish directory.</summary>
public sealed record PublishedFile(string SourcePath, string RelativePath);

public interface ISiteFileResolver
{
    // What publish copies once the site files join the project's other files: at each path a
    // declared site file (the first declared), else the file publish already copied, else a
    // fallback site file.
    SitePublication Resolve(IReadOnlyList<PublishedFile> published, IReadOnlyList<SiteFile> siteFiles);
}

/// <summary>
/// The published files a site file replaces, and the site files publish adds, each by its index,
/// with its normalized path.
/// </summary>
public sealed record SitePublication(IReadOnlySet<int> Replaced, IReadOnlyList<(int Index, string SitePath)> Added);

public sealed class SiteFileResolver : ISiteFileResolver
{
    public SitePublication Resolve(IReadOnlyList<PublishedFile> published, IReadOnlyList<SiteFile> siteFiles)
    {
        ArgumentNullException.ThrowIfNull(published);
        ArgumentNullException.ThrowIfNull(siteFiles);
        var declared = new Dictionary<string, int>(StringComparer.Ordinal);
        var fallbacks = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < siteFiles.Count; index++)
            (siteFiles[index].Fallback ? fallbacks : declared).TryAdd(SitePaths.Normalize(siteFiles[index].SitePath), index);
        var publishedPaths = new HashSet<string>(StringComparer.Ordinal);
        var replaced = new HashSet<int>();
        for (var index = 0; index < published.Count; index++)
        {
            var path = SitePaths.Normalize(published[index].RelativePath);
            publishedPaths.Add(path);
            if (declared.ContainsKey(path)) replaced.Add(index);
        }
        return new(replaced, [.. declared.Select(file => (file.Value, file.Key)), .. fallbacks
            .Where(file => !declared.ContainsKey(file.Key) && !publishedPaths.Contains(file.Key))
            .Select(file => (file.Value, file.Key))]);
    }
}

public interface ISiteMapWriter
{
    // The map dotnet run's development server serves the site from: each path a URL can name,
    // and the file served there.
    void Write(string outputPath, IReadOnlyList<PublishedFile> files);
}

/// <summary>
/// Writes a site's map. It is written on every build, so the development server, which reloads
/// the site's pages when the map changes, reloads them after every build; it is written whole,
/// through a temporary file, so the server never reads part of it.
/// </summary>
public sealed class SiteMapWriter : ISiteMapWriter
{
    public void Write(string outputPath, IReadOnlyList<PublishedFile> files)
    {
        ArgumentException.ThrowIfNullOrEmpty(outputPath);
        ArgumentNullException.ThrowIfNull(files);
        var map = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var path = SitePaths.Normalize(file.RelativePath);
            // A path outside the publish directory is published there, but no URL names it.
            if (path.Length == 0 || path.StartsWith('/') || Path.IsPathRooted(path) || path.Split('/').Contains(".."))
                continue;
            map[path] = Path.GetFullPath(file.SourcePath);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        var temporary = outputPath + ".tmp";
        using (var stream = File.Create(temporary))
        using (var json = new Utf8JsonWriter(stream, new() { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteNumber("version", 1);
            json.WriteStartObject("files");
            foreach (var (path, source) in map) json.WriteString(path, source);
            json.WriteEndObject();
            json.WriteEndObject();
        }
        File.Move(temporary, outputPath, overwrite: true);
    }
}

internal static class SitePaths
{
    public static string Normalize(string path) => path.Replace('\\', '/');
}
