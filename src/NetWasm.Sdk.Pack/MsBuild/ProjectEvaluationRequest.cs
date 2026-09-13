namespace NetWasm.Sdk.Pack.MsBuild;

public sealed record ProjectEvaluationRequest(
    string TargetFramework,
    string? Configuration = null,
    string? Platform = null,
    string? RuntimeIdentifier = null);
