namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed record RuntimeFunctionAppendResult(
    int FilterDispatcherIndex,
    int FinalizerDispatcherIndex,
    int EntryPointIndex,
    bool HasFinalizers);
