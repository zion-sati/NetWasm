using System.Collections.Immutable;

namespace NetWasm.Sdk.Pack.Policies;

public sealed class ProfileRegistry : IProfileResolver
{
    private readonly ImmutableDictionary<string, TargetProfile> profiles;

    public ProfileRegistry(IEnumerable<TargetProfile>? profiles = null)
    {
        var values = (profiles ?? [TargetProfile.NetWasmV01]).ToArray();
        if (values.Any(static profile => profile is null))
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK001, "A framework profile registration is missing.");
        }

        var duplicate = values
            .GroupBy(static profile => profile.Alias, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK016, "A framework profile alias is registered more than once.");
        }

        var duplicateGroup = values
            .GroupBy(static profile => profile.CanonicalDependencyGroup, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicateGroup is not null)
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK016, "A canonical framework group is registered more than once.");
        }

        foreach (var profile in values)
        {
            ValidateProfile(profile);
        }

        this.profiles = values.ToImmutableDictionary(static profile => profile.Alias, StringComparer.OrdinalIgnoreCase);
    }

    public TargetProfile Resolve(string profileAlias)
    {
        if (string.IsNullOrWhiteSpace(profileAlias) || !profiles.TryGetValue(profileAlias, out var profile))
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK001, "The requested framework profile is not registered.");
        }

        return profile;
    }

    private static void ValidateProfile(TargetProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Alias) || string.IsNullOrWhiteSpace(profile.Identifier) ||
            string.IsNullOrWhiteSpace(profile.Version) || string.IsNullOrWhiteSpace(profile.CanonicalFolder) ||
            string.IsNullOrWhiteSpace(profile.CanonicalDependencyGroup))
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK001, "A framework profile is incomplete.");
        }

        if (!string.Equals(profile.CanonicalFolder, profile.CanonicalDependencyGroup, StringComparison.Ordinal) ||
            profile.CanonicalFolder.Any(char.IsControl) || profile.CanonicalDependencyGroup.Any(char.IsControl))
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK002, "A framework profile has inconsistent canonical identity.");
        }

        var expected = $"{profile.Identifier},Version={profile.Version}";
        if (!string.Equals(profile.CanonicalFolder, expected, StringComparison.Ordinal))
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK002, "A framework profile has a non-canonical folder identity.");
        }
    }
}
