namespace NetWasm.Hosting.Execution;

internal interface INetWasmArtifactPathResolver
{
    NetWasmArtifactPaths Resolve(string sourcePath);
}
