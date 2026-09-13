namespace NetWasm.Hosting.Deployment;

/// <summary>Identifies one exact generated component boundary.</summary>
public sealed record CanonicalComponentAdapterRequest(
    string ContractKey,
    string JcoVersion);
