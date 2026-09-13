using System.Collections.Immutable;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed record ManagedMethodSequenceEmission(
    ImmutableDictionary<int, int> OriginalBlockEmissionCounts);
