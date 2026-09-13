namespace NetWasm.Compiler.ComponentModel.ManagedExecutables;

public sealed record NetWasmHostComponentShimRequest(
    string OutputPath,
    ComponentTarget Target);
