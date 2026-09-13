namespace NetWasm.Hosting.Execution;

/// <summary>An exact application module bound to a hashed deployment asset.</summary>
public sealed record NetWasmApplicationImport(string Module, string ArtifactPath, string Sha256);
