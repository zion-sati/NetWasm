namespace NetWasm.Sdk.Pack.Archives;

public sealed record PackageOutput(string Path, string Sha256, PackageIdentity Identity, bool IsSigned = false);
