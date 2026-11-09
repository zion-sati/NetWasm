using NetWasm.Compiler.ComponentModel.Optimization;

namespace NetWasm.Compiler.Tasks.MsBuild;

// The wasm-opt settings the link tasks take beside the managed ones: convergence is opt-in,
// and an empty NetWasmOptimizerLowMemoryUnused leaves the fold off.
internal static class OptimizerOverridesParser
{
    public static WasmOptimizationOverrides? Apply(
        WasmOptimizationOverrides? overrides,
        bool converge,
        string lowMemoryUnused)
    {
        if (converge)
            overrides = (overrides ?? new()) with { Converge = true };
        return ParseLowMemoryUnused(lowMemoryUnused) is { } value
            ? (overrides ?? new()) with { LowMemoryUnused = value }
            : overrides;
    }

    private static bool? ParseLowMemoryUnused(string value) => value.ToLowerInvariant() switch
    {
        "" => null,
        "true" => true,
        "false" => false,
        _ => throw new InvalidOperationException(
            "NetWasmOptimizerLowMemoryUnused must be true, false or empty."),
    };
}
