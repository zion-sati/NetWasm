using System.Collections.Immutable;

namespace NetWasm.Sdk.Pack.Profiles;

public sealed class TargetIdentityPolicy : ITargetIdentityValidator
{
    public ImmutableArray<TargetOutputIdentity> Validate(IReadOnlyList<TargetOutputIdentity> identities)
    {
        ArgumentNullException.ThrowIfNull(identities);
        var values = identities.ToImmutableArray();
        if (values.Any(static identity => identity is null))
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK001, "A target output identity is missing.");
        }

        if (values.Select(static identity => identity.Alias).Distinct(StringComparer.OrdinalIgnoreCase).Count() != values.Length ||
            values.Select(static identity => identity.AssetFolder).Distinct(StringComparer.Ordinal).Count() != values.Length)
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK016, "A target output identity is ambiguous or duplicated.");
        }

        if (!values.IsDefaultOrEmpty && !values.Any(static identity =>
                string.Equals(identity.Alias, TargetProfile.NetWasmV01.Alias, StringComparison.OrdinalIgnoreCase)))
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK001, "A custom NetWasm target identity is required for SDK-owned packing.");
        }

        foreach (var identity in values)
        {
            if (string.IsNullOrWhiteSpace(identity.Alias) || string.IsNullOrWhiteSpace(identity.Identifier) ||
                string.IsNullOrWhiteSpace(identity.Version) || string.IsNullOrWhiteSpace(identity.AssetFolder))
            {
                throw new NetWasmPackException(NetWasmPackErrorCode.NWPK001, "A target output identity is incomplete.");
            }

            if (identity.AssetFolder.Contains("..", StringComparison.Ordinal) || identity.AssetFolder.Any(char.IsControl) ||
                identity.AssetFolder.Contains('\\') || identity.AssetFolder.StartsWith('/'))
            {
                throw new NetWasmPackException(NetWasmPackErrorCode.NWPK009, "A target output identity has an unsafe asset folder.");
            }

            var isNetWasm = string.Equals(identity.Alias, TargetProfile.NetWasmV01.Alias, StringComparison.OrdinalIgnoreCase);
            if (isNetWasm && (!string.Equals(identity.Identifier, TargetProfile.NetWasmV01.Identifier, StringComparison.Ordinal) ||
                              !string.Equals(identity.Version, TargetProfile.NetWasmV01.Version, StringComparison.Ordinal) ||
                              !string.Equals(identity.AssetFolder, TargetProfile.NetWasmV01.CanonicalFolder, StringComparison.Ordinal) ||
                              !string.Equals(identity.DependencyGroup, TargetProfile.NetWasmV01.CanonicalDependencyGroup, StringComparison.Ordinal)))
            {
                throw new NetWasmPackException(NetWasmPackErrorCode.NWPK002, "The NetWasm target identity is not canonical.");
            }

            if (!isNetWasm && !string.IsNullOrWhiteSpace(identity.DependencyGroup))
            {
                throw new NetWasmPackException(NetWasmPackErrorCode.NWPK002, "A desktop target cannot define a custom dependency group.");
            }
        }

        return values;
    }
}
