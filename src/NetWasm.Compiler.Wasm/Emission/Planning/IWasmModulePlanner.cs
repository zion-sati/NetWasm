using System.Collections.Immutable;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal interface IWasmModulePlanner
{
    WasmModulePlan Build(
        WasmEmissionRequest request,
        ImmutableArray<StructuredMethodEmission> methodEmissions);
}
