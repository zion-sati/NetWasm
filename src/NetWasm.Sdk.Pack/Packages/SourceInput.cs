namespace NetWasm.Sdk.Pack.Packages;

public sealed record SourceInput(string SourcePath, string TargetPath, string? SourceRoot = null)
{
    public string? TargetFrameworkAlias { get; init; }
    public string? ExpectedSha256 { get; init; }
    public long? ExpectedLength { get; init; }
}
