namespace NetWasm.Compiler.Tests.Correctness;

internal sealed record LinkedCorpusToolEnvironment(
    string? EmsdkRoot,
    string? NodePath,
    string? WasmToolsPath);

internal interface ILinkedCorpusToolPathsProvider
{
    LinkedCorpusToolPaths Get();
}

internal sealed class LinkedCorpusToolPathsProvider(
    LinkedCorpusToolEnvironment environment) : ILinkedCorpusToolPathsProvider
{
    public LinkedCorpusToolPaths Get()
    {
        ArgumentNullException.ThrowIfNull(environment);
        var emsdk = RequireAbsolute(environment.EmsdkRoot, "NETWASM_EMSDK_ROOT");
        var node = RequireAbsolute(environment.NodePath, "NETWASM_NODE_PATH");
        var wasmTools = RequireAbsolute(
            environment.WasmToolsPath, "NETWASM_WASM_TOOLS_PATH");
        return new(
            emsdk,
            Path.Combine(emsdk, "upstream", "bin", "wasm-merge"),
            Path.Combine(emsdk, "upstream", "bin", "wasm-opt"),
            wasmTools,
            node);
    }

    private static string RequireAbsolute(string? path, string name)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            throw new InvalidOperationException(
                $"{name} must identify an absolute linked-qualification tool path");
        }
        return Path.GetFullPath(path);
    }
}
