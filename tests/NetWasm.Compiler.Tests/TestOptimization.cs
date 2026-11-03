using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tests;

// The preset that a corpus fixture or a compilation scenario naming none
// compiles at: Oz, or NETWASM_TEST_OPTIMIZATION's, so a lane runs every one of
// them at another preset against the same expectations.
internal static class TestOptimization
{
    public const string EnvironmentVariable = "NETWASM_TEST_OPTIMIZATION";

    public static FinalWasmOptimization Default { get; } =
        Parse(Environment.GetEnvironmentVariable(EnvironmentVariable));

    internal static FinalWasmOptimization Parse(string? value) =>
        string.IsNullOrEmpty(value)
            ? FinalWasmOptimization.Oz
            : Enum.GetNames<FinalWasmOptimization>().Contains(value, StringComparer.Ordinal)
                ? Enum.Parse<FinalWasmOptimization>(value)
                : throw new InvalidOperationException(
                    $"{EnvironmentVariable} must be one of {string.Join(", ", Enum.GetNames<FinalWasmOptimization>())}");
}
