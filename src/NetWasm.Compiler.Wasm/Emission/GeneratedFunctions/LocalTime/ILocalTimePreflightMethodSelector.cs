using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions.LocalTime;

internal interface ILocalTimePreflightMethodSelector
{
    EntityKey? Select(WasmEmissionRequest request);
}
