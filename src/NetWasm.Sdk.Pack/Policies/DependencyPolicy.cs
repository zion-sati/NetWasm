using NuGet.Packaging;
using NuGet.Versioning;

namespace NetWasm.Sdk.Pack.Policies;

public sealed class DependencyPolicy : IDependencyValidator
{
    private static readonly string[] AssetOrder =
        ["compile", "runtime", "contentFiles", "build", "buildMultitargeting", "buildTransitive", "analyzers", "native"];

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

        if (dependency.IsDevelopmentDependency || ContainsAsset(dependency.PrivateAssets, "all"))
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK007, "A private or development dependency cannot enter a public dependency group.");
        }

        if (!string.Equals(dependency.TargetFramework, profile.CanonicalDependencyGroup, StringComparison.Ordinal) ||
            (dependency.TargetFrameworkAlias is not null && !string.Equals(dependency.TargetFrameworkAlias, profile.Alias, StringComparison.OrdinalIgnoreCase)))
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK006, "A dependency does not belong to the exact registered framework group.");
        }

        if (!TryParseAssets(dependency.IncludeAssets, out var included) ||
            !TryParseAssets(dependency.ExcludeAssets, out var excluded) ||
            !TryParseAssets(dependency.PrivateAssets, out var privateAssets))
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK006, "A dependency asset filter is invalid.");
        }

        excluded.UnionWith(privateAssets.Where(static asset => !asset.Equals("contentFiles", StringComparison.OrdinalIgnoreCase)));

        return new CanonicalPackageDependency(dependency.Id, dependency.VersionRange)
        {
            IncludeAssets = FormatAssets(included, "all"),
            ExcludeAssets = FormatAssets(excluded, "none"),
            PrivateAssets = dependency.PrivateAssets
        };
    }

    private static bool ContainsAsset(string value, string asset) =>
        value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(asset, StringComparer.OrdinalIgnoreCase);

    private static bool TryParseAssets(string value, out HashSet<string> assets)
    {
        assets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl))
        {
            return false;
        }

        var values = value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var asset in values)
        {
            if (asset.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                if (values.Length != 1)
                {
                    return false;
                }

                continue;
            }

            if (asset.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                if (values.Length != 1)
                {
                    return false;
                }

                assets.Add("all");
                continue;
            }

            var canonicalAsset = AssetOrder.FirstOrDefault(candidate => candidate.Equals(asset, StringComparison.OrdinalIgnoreCase));
            if (canonicalAsset is null)
            {
                return false;
            }

            assets.Add(canonicalAsset);
        }

        return values.Length > 0;
    }

    private static string FormatAssets(HashSet<string> assets, string defaultValue)
    {
        if (assets.Contains("all"))
        {
            return "all";
        }

        var ordered = AssetOrder.Where(assets.Contains).ToArray();
        return ordered.Length == 0 ? defaultValue : string.Join(',', ordered);
    }
}
