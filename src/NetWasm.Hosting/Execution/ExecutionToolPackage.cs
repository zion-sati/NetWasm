namespace NetWasm.Hosting.Execution;

/// <summary>A restored tool package and the digest of its immutable package archive.</summary>
public sealed record ExecutionToolPackage(string Id, string Version, string RootPath, string Sha256);
