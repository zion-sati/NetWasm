using Microsoft.Build.Framework;

namespace NetWasm.Sdk.Pack.MsBuild;

public sealed class MsBuildPackInputAdapter : IMsBuildPackInputAdapter
{
    public CanonicalPackInputs Adapt(
        string id,
        string version,
        string authors,
        string description,
        string outputPath,
        IReadOnlyList<ITaskItem> files,
        IReadOnlyList<ITaskItem> dependencies,
        string canonicalTargetFramework)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(dependencies);

        return new CanonicalPackInputs(
            id,
            version,
            authors,
            description,
            outputPath,
            files.SelectMany(AdaptFiles).ToArray(),
            dependencies.Select(AdaptDependency).ToArray(),
            canonicalTargetFramework);
    }

    private static IEnumerable<CanonicalPackageFileInput> AdaptFiles(ITaskItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var source = FirstMetadata(item, "SourcePath", "FullPath");
        if (ParseBoolean(OptionalMetadata(item, "NetWasmRawContent")))
        {
            return ResolveContentTargets(item, source).Select(target => CreateFileInput(item, source, target));
        }

        return [CreateFileInput(item, source, FirstMetadata(item, "PackagePath", "TargetPath"))];
    }

    private static CanonicalPackageFileInput CreateFileInput(ITaskItem item, string source, string target) =>
        new(source, target)
        {
            TargetFrameworkAlias = OptionalMetadata(item, "TargetFrameworkAlias"),
            SourceRoot = OptionalMetadata(item, "SourceRoot"),
            Kind = ParseKind(OptionalMetadata(item, "PackKind")),
            ExcludeFromMainPackage = ParseBoolean(OptionalMetadata(item, "ExcludeFromMainPackage")),
            ExpectedSha256 = OptionalMetadata(item, "ExpectedSha256", "Sha256"),
            ExpectedLength = ParseLength(OptionalMetadata(item, "ExpectedLength", "Length"))
        };

    private static IEnumerable<string> ResolveContentTargets(ITaskItem item, string source)
    {
        var packagePathSpecified = item.MetadataNames.Cast<string>()
            .Contains("PackagePath", StringComparer.OrdinalIgnoreCase);
        var fileName = Path.GetFileName(source);
        IEnumerable<string> targetDirectories;

        if (packagePathSpecified)
        {
            targetDirectories = item.GetMetadata("PackagePath")
                .Split(';', StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            var relativePath = NormalizeRelativeIdentity(item.ItemSpec, fileName);
            return SplitMetadata(item.GetMetadata("NetWasmContentTargetFolders"))
                .Select(folder => folder.Equals("contentFiles", StringComparison.OrdinalIgnoreCase)
                    ? $"contentFiles/any/any/{relativePath}"
                    : $"{folder}/{relativePath}");
        }

        var recursiveDirectory = FirstNonEmptyMetadata(item, "RecursiveDir", "NuGetRecursiveDir");
        return targetDirectories.Select(target => ResolveExplicitTarget(target, recursiveDirectory, fileName));
    }

    private static string ResolveExplicitTarget(string target, string? recursiveDirectory, string fileName)
    {
        // NuGet uses a single separator to designate the package root.
        if (target is "/" or "\\")
        {
            target = string.Empty;
        }

        if (!string.IsNullOrEmpty(recursiveDirectory) && !ExtensionsMatch(fileName, target))
        {
            target = Path.Combine(target, recursiveDirectory);
        }

        if (!ExtensionsMatch(fileName, target))
        {
            target = Path.Combine(target, fileName);
        }

        return target.Replace('\\', '/');
    }

    private static string NormalizeRelativeIdentity(string identity, string fileName)
    {
        var relativePath = Path.IsPathRooted(identity) ? fileName : identity;
        return relativePath.Replace('\\', '/').TrimStart('/');
    }

    private static bool ExtensionsMatch(string source, string target)
    {
        var sourceExtension = Path.GetExtension(source);
        return sourceExtension.Length > 0 &&
            sourceExtension.Equals(Path.GetExtension(target), StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> SplitMetadata(string value) =>
        value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private static string? FirstNonEmptyMetadata(ITaskItem item, params string[] names)
    {
        foreach (var name in names)
        {
            var value = item.GetMetadata(name);
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }
        }

        return null;
    }
    private static CanonicalPackageDependencyInput AdaptDependency(ITaskItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new CanonicalPackageDependencyInput(
            item.ItemSpec,
            OptionalMetadata(item, "VersionRange", "Version") ?? string.Empty,
            FirstMetadata(item, "TargetFramework", "TargetFrameworkMoniker"))
        {
            TargetFrameworkAlias = OptionalMetadata(item, "TargetFrameworkAlias"),
            IncludeAssets = OptionalMetadata(item, "IncludeAssets") ?? "all",
            ExcludeAssets = OptionalMetadata(item, "ExcludeAssets") ?? "none",
            PrivateAssets = OptionalMetadata(item, "PrivateAssets") ?? "none",
            IsDevelopmentDependency = ParseBoolean(OptionalMetadata(item, "DevelopmentDependency"))
        };
    }

    private static string FirstMetadata(ITaskItem item, params string[] names)
    {
        foreach (var name in names)
        {
            var value = item.GetMetadata(name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return item.ItemSpec;
    }

    private static string? OptionalMetadata(ITaskItem item, string name)
    {
        var value = item.GetMetadata(name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string? OptionalMetadata(ITaskItem item, params string[] names)
    {
        foreach (var name in names)
        {
            var value = item.GetMetadata(name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static long? ParseLength(string? value) => long.TryParse(value, out var length) && length >= 0 ? length : null;

    private static bool ParseBoolean(string? value) =>
        bool.TryParse(value, out var result) && result;

    private static PackageFileKind ParseKind(string? value) =>
        Enum.TryParse<PackageFileKind>(value, ignoreCase: true, out var kind) ? kind : PackageFileKind.Other;
}
