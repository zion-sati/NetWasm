namespace NetWasm.Compiler.Tests.Correctness;

internal sealed record LinkedCorpusToolEnvironment(
    string? EmsdkRoot,
    string? NodePath,
    string? WasmToolsPath,
    string? Collector = null,
    string? GcStress = null);

// What each linked collector gives a fixture. Bump never collects, finalizes or
// clears weak references.
internal static class LinkedCorpusCollectorCapabilities
{
    public const OracleRuntimeCapabilities Collecting =
        OracleRuntimeCapabilities.GarbageCollection |
        OracleRuntimeCapabilities.Finalization |
        OracleRuntimeCapabilities.WeakReferenceClearing;

    public static OracleRuntimeCapabilities Of(string? collector) =>
        collector == "bump" ? OracleRuntimeCapabilities.None : Collecting;
}

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
        // Compact unless a run asks for another collector.
        var collector = string.IsNullOrEmpty(environment.Collector) ? "compact" : environment.Collector;
        if (collector is not ("compact" or "netwasm" or "bump"))
        {
            throw new InvalidOperationException(
                "NETWASM_CORPUS_COLLECTOR must be compact, netwasm or bump");
        }
        // NETWASM_CORPUS_GC_STRESS=1 runs every linked fixture under GC stress,
        // to qualify roots across the corpus.
        var gcStress = environment.GcStress switch
        {
            null or "" or "0" => false,
            "1" => true,
            _ => throw new InvalidOperationException("NETWASM_CORPUS_GC_STRESS must be 0 or 1"),
        };
        if (gcStress && collector == "bump")
        {
            throw new InvalidOperationException("NETWASM_CORPUS_GC_STRESS requires a collecting collector");
        }
        return new(
            emsdk,
            Path.Combine(emsdk, "upstream", "bin", "wasm-merge"),
            Path.Combine(emsdk, "upstream", "bin", "wasm-opt"),
            wasmTools,
            node,
            collector,
            gcStress);
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
