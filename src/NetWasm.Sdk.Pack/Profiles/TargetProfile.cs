namespace NetWasm.Sdk.Pack.Profiles;

/// <summary>One registered framework identity; each spelling has a separate meaning.</summary>
public sealed record TargetProfile(
    string Alias,
    string Identifier,
    string Version,
    string CanonicalFolder,
    string CanonicalDependencyGroup)
{
    public static TargetProfile NetWasmV01 { get; } = new(
        "netwasm0.1",
        "NetWasm",
        "v0.1",
        "NetWasm,Version=v0.1",
        "NetWasm,Version=v0.1");
}
