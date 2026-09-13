namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed record ManagedMethodEmissionRecord(
    string Identity,
    string MethodKey,
    ManagedMethodBodyEmission Emission);
