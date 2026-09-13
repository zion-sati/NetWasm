using System.Collections.Immutable;

namespace NetWasm.TimeZones;

internal sealed class TimeZoneCatalogReader(
    IIanaVersionReader versions,
    ITzifParser parser) : ITimeZoneCatalogReader
{
    private static readonly string[] MetadataNames =
    [
        "localtime", "posixrules", "tzdata.zi", "version", "+VERSION",
        "zone.tab", "zone1970.tab", "iso3166.tab", "leapseconds",
        "leap-seconds.list",
    ];
    private readonly IIanaVersionReader _versions =
        versions ?? throw new ArgumentNullException(nameof(versions));
    private readonly ITzifParser _parser =
        parser ?? throw new ArgumentNullException(nameof(parser));

    public TimeZoneCatalog Read(string sourceDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceDirectory);
        var root = Path.GetFullPath(sourceDirectory);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException(root);
        }
        var definitions = ImmutableDictionary.CreateBuilder<string, TimeZoneDefinition>(
            StringComparer.Ordinal);
        var aliases = ImmutableDictionary.CreateBuilder<string, string>(
            StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(
            root,
            "*",
            SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var name = Normalize(root, path);
            if (IsMetadata(name))
            {
                continue;
            }
            var file = new FileInfo(path);
            if (file.LinkTarget is not null)
            {
                var target = file.ResolveLinkTarget(returnFinalTarget: true)!;
                var targetPath = Path.GetFullPath(target.FullName);
                if (!targetPath.StartsWith(root + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Timezone alias '{name}' escapes the source directory.");
                }
                aliases.Add(name, Normalize(root, targetPath));
                continue;
            }
            var contents = File.ReadAllBytes(path);
            if (contents.Length < 4 ||
                contents[0] != 'T' || contents[1] != 'Z' ||
                contents[2] != 'i' || contents[3] != 'f')
            {
                continue;
            }
            definitions.Add(name, _parser.Parse(name, contents));
        }
        foreach (var alias in aliases)
        {
            if (!definitions.ContainsKey(alias.Value))
            {
                throw new InvalidOperationException(
                    $"Timezone alias '{alias.Key}' targets missing zone '{alias.Value}'.");
            }
        }
        if (definitions.Count == 0)
        {
            throw new InvalidOperationException("Timezone source contains no TZif zones.");
        }
        return new TimeZoneCatalog(
            _versions.Read(root),
            definitions.ToImmutable(),
            aliases.ToImmutable());
    }

    private static string Normalize(string root, string path) =>
        Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    private static bool IsMetadata(string name) =>
        name.StartsWith("posix/", StringComparison.Ordinal) ||
        name.StartsWith("right/", StringComparison.Ordinal) ||
        Array.IndexOf(MetadataNames, name) >= 0;
}
