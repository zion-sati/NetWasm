namespace NetWasm.Sdk.Pack.Packages;

public sealed record PackageIdentity(string Id, string Version, string PackageType = "Dependency");
