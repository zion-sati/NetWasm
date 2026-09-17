using System.Collections.Immutable;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed record ManagedMethodBodyEmission(
    byte[] Body,
    int WasmInstructionCount,
    long CompileDurationTicks,
    long? PeakObservedManagedMemoryBytes,
    string MethodKey,
    FilterEnvironmentLayout FilterEnvironment,
    ImmutableDictionary<int, int> OriginalBlockEmissionCounts);
