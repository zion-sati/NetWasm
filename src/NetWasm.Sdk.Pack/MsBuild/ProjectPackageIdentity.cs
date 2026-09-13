namespace NetWasm.Sdk.Pack.MsBuild;

public sealed record ProjectPackageIdentity(string PackageId, string PackageVersion, bool IsPackable = true);
