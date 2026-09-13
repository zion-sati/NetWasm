using NuGet.Packaging;
using NuGet.Versioning;

namespace NetWasm.Sdk.Pack.Policies;

public sealed class DependencyPolicy : IDependencyValidator
{
    public CanonicalPackageDependency Validate(CanonicalPackageDependencyInput dependency, TargetProfile profile)
    {
        ArgumentNullException.ThrowIfNull(dependency);
        ArgumentNullException.ThrowIfNull(profile);

        if (string.IsNullOrWhiteSpace(dependency.Id) || !PackageIdValidator.IsValidPackageId(dependency.Id))
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK006, "A dependency ID is not a valid NuGet identifier.");
        }

        if (!VersionRange.TryParse(dependency.VersionRange, allowFloating: false, out _))
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK006, "A dependency version range is not valid NuGet syntax.");
        }

        if (dependency.IsDevelopmentDependency || !string.Equals(dependency.PrivateAssets, "none", StringComparison.OrdinalIgnoreCase))
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK007, "A private or development dependency cannot enter a public dependency group.");
        }

        if (!string.Equals(dependency.TargetFramework, profile.CanonicalDependencyGroup, StringComparison.Ordinal) ||
            (dependency.TargetFrameworkAlias is not null && !string.Equals(dependency.TargetFrameworkAlias, profile.Alias, StringComparison.OrdinalIgnoreCase)))
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK006, "A dependency does not belong to the exact registered framework group.");
        }

        if (ContainsInvalidMetadata(dependency.IncludeAssets) || ContainsInvalidMetadata(dependency.ExcludeAssets))
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK006, "A dependency asset filter is invalid.");
        }

        return new CanonicalPackageDependency(dependency.Id, dependency.VersionRange)
        {
            IncludeAssets = dependency.IncludeAssets,
            ExcludeAssets = dependency.ExcludeAssets,
            PrivateAssets = dependency.PrivateAssets
        };
    }

    private static bool ContainsInvalidMetadata(string value) =>
        string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl) || value.Any(char.IsWhiteSpace);
}
