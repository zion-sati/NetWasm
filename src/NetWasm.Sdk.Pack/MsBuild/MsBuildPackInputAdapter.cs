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
            files.Select(AdaptFile).ToArray(),
            dependencies.Select(AdaptDependency).ToArray(),
            canonicalTargetFramework);
    }

    private static CanonicalPackageFileInput AdaptFile(ITaskItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var source = FirstMetadata(item, "SourcePath", "FullPath");
        var target = FirstMetadata(item, "PackagePath", "TargetPath");
        return new CanonicalPackageFileInput(source, target)
        {
            TargetFrameworkAlias = OptionalMetadata(item, "TargetFrameworkAlias"),
            SourceRoot = OptionalMetadata(item, "SourceRoot"),
            Kind = ParseKind(OptionalMetadata(item, "PackKind")),
            ExcludeFromMainPackage = ParseBoolean(OptionalMetadata(item, "ExcludeFromMainPackage")),
            ExpectedSha256 = OptionalMetadata(item, "ExpectedSha256", "Sha256"),
            ExpectedLength = ParseLength(OptionalMetadata(item, "ExpectedLength", "Length"))
        };
    }

    private static CanonicalPackageDependencyInput AdaptDependency(ITaskItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new CanonicalPackageDependencyInput(
            item.ItemSpec,
            FirstMetadata(item, "VersionRange", "Version"),
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
