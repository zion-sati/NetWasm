namespace NetWasm.Hosting.Execution;

internal sealed record NetWasmArtifactPaths(
    string SourcePath,
    string DescriptorPath,
    string RequestPath);
