using System.Collections.Immutable;

namespace NetWasm.Compiler.ComponentModel;

public sealed record ExternalToolCommand(
    string Executable,
    ImmutableArray<string> ArgumentPrefix)
{
    // The packaged wasm-tools runs as a WASI guest under Node, which warns that
    // WASI is experimental on every run; without this flag the warning would
    // lead every diagnostic the tool reports.
    public static ExternalToolCommand ForWasmTools(string node, string command, string module) =>
        new(node, ["--disable-warning=ExperimentalWarning", command, module]);
}
