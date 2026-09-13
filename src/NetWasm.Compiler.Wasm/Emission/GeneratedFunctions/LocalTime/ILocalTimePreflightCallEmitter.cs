using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions.LocalTime;

internal interface ILocalTimePreflightCallEmitter
{
    bool Emit(
        GeneratedFunctionWriterLease code,
        WasmEmissionRequest request,
        IFunctionIndexResolver functionIndices);
}
