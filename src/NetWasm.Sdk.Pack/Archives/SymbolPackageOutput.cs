namespace NetWasm.Sdk.Pack.Archives;

public sealed record SymbolPackageOutput(string Path, string Sha256, PackageIdentity Identity, bool IsSigned = false);
