using NuGet.Packaging;
using NuGet.Versioning;

namespace NetWasm.Sdk.Pack.Policies;

public sealed class PackageIdentityValidator : IPackageIdentityValidator
{
    public PackageIdentity Validate(PackageIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        if (string.IsNullOrWhiteSpace(identity.Id) || !PackageIdValidator.IsValidPackageId(identity.Id))
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK006, "The package ID is not a valid NuGet identifier.");
        }

        if (string.IsNullOrWhiteSpace(identity.PackageType) || ContainsInvalidCharacters(identity.PackageType))
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK006, "The package type is invalid.");
        }

        if (string.IsNullOrWhiteSpace(identity.Version) || identity.Version.Any(char.IsWhiteSpace) ||
            identity.Version.Any(char.IsControl) || identity.Version.Contains('/') || identity.Version.Contains('\\') ||
            !NuGetVersion.TryParse(identity.Version, out var version))
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK006, "The package version is not valid NuGet SemVer.");
        }

        return identity with { Version = version.ToNormalizedString() };
    }

    private static bool ContainsInvalidCharacters(string value) =>
        value.Any(char.IsControl) || value.Any(char.IsWhiteSpace) || value.Contains('/') || value.Contains('\\');
}
