using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Wasm.Emission.Methods;

namespace NetWasm.Compiler.Wasm.Emission.Results;

internal sealed record WasmModuleEmissionBuildRequest(
    WasmModuleBuildRequest Module,
    int StaticDataEnd,
    IReadOnlyList<ManagedMethodEmissionRecord> ManagedMethodEmissions,
    ImmutableArray<WasmStackTraceSymbol> StackTraceSymbols,
    ImmutableArray<string> RuntimeFeatures);
