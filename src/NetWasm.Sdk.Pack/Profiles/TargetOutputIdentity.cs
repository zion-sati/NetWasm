namespace NetWasm.Sdk.Pack.Profiles;

/// <summary>
/// Evaluated target output identity. Unlike <see cref="TargetProfile"/>, a
/// desktop target may have an ordinary short asset folder and no public
/// dependency-group spelling. The adapter preserves both identities without
/// asking NuGet to reformat either one.
/// </summary>
public sealed record TargetOutputIdentity(
    string Alias,
    string Identifier,
    string Version,
    string AssetFolder,
    string? DependencyGroup,
    bool SuppressDependencies = false)
{
    public static TargetOutputIdentity NetWasmV01 { get; } = new(
        TargetProfile.NetWasmV01.Alias,
        TargetProfile.NetWasmV01.Identifier,
        TargetProfile.NetWasmV01.Version,
        TargetProfile.NetWasmV01.CanonicalFolder,
        TargetProfile.NetWasmV01.CanonicalDependencyGroup);
}
