namespace NetWasm.Sdk.Pack.Packages;

public sealed record SymbolInput(string SourcePath, string TargetPath, string Format = "snupkg")
{
    public string? TargetFrameworkAlias { get; init; }
    public string? ExpectedSha256 { get; init; }
    public long? ExpectedLength { get; init; }
}
