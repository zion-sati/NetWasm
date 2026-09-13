namespace NetWasm.Testing.VSTest.Configuration;

internal sealed record NetWasmRunConfiguration(
    string TargetFrameworkMoniker,
    string? DotnetHostPath);
