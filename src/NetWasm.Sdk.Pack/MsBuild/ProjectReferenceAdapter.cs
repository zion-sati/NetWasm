using Microsoft.Build.Framework;

namespace NetWasm.Sdk.Pack.MsBuild;

public sealed class ProjectReferenceAdapter : IProjectReferenceAdapter
{
    private readonly IProjectPackageIdentityResolver identityResolver;

    public ProjectReferenceAdapter(IProjectPackageIdentityResolver identityResolver)
    {
        this.identityResolver = identityResolver ?? throw new ArgumentNullException(nameof(identityResolver));
    }

    public CanonicalPackageDependencyInput? Adapt(ITaskItem item, string canonicalTargetFramework)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (bool.TryParse(item.GetMetadata("Pack"), out var pack) && !pack)
        {
            return null;
        }

        var targetFramework = FirstMetadata(item, "TargetFramework", "TargetFrameworkMoniker") ?? canonicalTargetFramework;
        var evaluationTargetFramework = FirstMetadata(item, "TargetFrameworkAlias") ?? targetFramework;
        if (string.Equals(evaluationTargetFramework, TargetProfile.NetWasmV01.CanonicalDependencyGroup, StringComparison.Ordinal))
        {
            evaluationTargetFramework = TargetProfile.NetWasmV01.Alias;
        }
        var packageId = FirstMetadata(item, "PackageId", "ProjectReferencePackageId");
        if (string.IsNullOrWhiteSpace(packageId) &&
            string.Equals(FirstMetadata(item, "ProjectReferenceResolution"), "resolved", StringComparison.OrdinalIgnoreCase) &&
            !LooksLikePath(item.ItemSpec))
        {
            // The SDK's resolved identity target places the authoritative
            // PackageId in ItemSpec and marks the record as resolved. A path
            // record never takes this branch.
            packageId = item.ItemSpec;
        }
        var projectIdentity = default(ProjectPackageIdentity);
        if (string.IsNullOrWhiteSpace(packageId))
        {
            var projectPath = FirstMetadata(item, "ProjectReferenceFullPath", "ProjectPath", "FullPath")!;
            projectIdentity = identityResolver.Resolve(
                projectPath,
                new ProjectEvaluationRequest(
                    evaluationTargetFramework,
                    OptionalMetadata(item, "Configuration"),
                    OptionalMetadata(item, "Platform"),
                    OptionalMetadata(item, "RuntimeIdentifier")));
            if (projectIdentity is { IsPackable: false })
            {
                return null;
            }

            packageId = projectIdentity?.PackageId;
        }

        if (string.IsNullOrWhiteSpace(packageId) || Path.IsPathRooted(packageId) || packageId.Contains(Path.DirectorySeparatorChar) || packageId.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK015, "A referenced project has no authoritative package identity for packing.");
        }

        var version = FirstMetadata(item, "VersionRange", "ProjectVersion", "Version", "PackageVersion") ?? projectIdentity?.PackageVersion;
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK015, "A referenced project has no authoritative package version for packing.");
        }

        return new CanonicalPackageDependencyInput(
            packageId,
            version,
            targetFramework)
        {
            IncludeAssets = OptionalMetadata(item, "IncludeAssets") ?? "all",
            ExcludeAssets = OptionalMetadata(item, "ExcludeAssets") ?? "none",
            PrivateAssets = OptionalMetadata(item, "PrivateAssets") ?? "none",
            IsDevelopmentDependency = bool.TryParse(item.GetMetadata("DevelopmentDependency"), out var development) && development,
            Origin = PackageDependencyOrigin.ProjectReference,
            TargetFrameworkAlias = OptionalMetadata(item, "TargetFrameworkAlias")
        };
    }

    private static string? FirstMetadata(ITaskItem item, params string[] names)
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

    private static string? OptionalMetadata(ITaskItem item, string name) => FirstMetadata(item, name);

    private static bool LooksLikePath(string value)
    {
        var normalized = value.Replace('\\', '/');
        return Path.IsPathRooted(value) || normalized.Contains('/') ||
            normalized.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) || normalized.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase);
    }
}
