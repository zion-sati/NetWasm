using System.Collections.Immutable;
using NetWasm.Compiler.Wasm.Emission.Methods;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed record FilterFuncletEmission(
    byte[] Body,
    ImmutableDictionary<int, int> OriginalBlockEmissionCounts);
