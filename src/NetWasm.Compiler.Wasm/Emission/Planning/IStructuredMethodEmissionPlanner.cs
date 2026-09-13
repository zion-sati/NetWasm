using System.Collections.Immutable;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal interface IStructuredMethodEmissionPlanner
{
    ImmutableArray<StructuredMethodEmission> Plan(WasmEmissionRequest request);
}
