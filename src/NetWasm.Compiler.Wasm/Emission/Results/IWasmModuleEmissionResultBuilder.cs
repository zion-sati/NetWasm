namespace NetWasm.Compiler.Wasm.Emission.Results;

internal interface IWasmModuleEmissionResultBuilder
{
    WasmModuleEmissionResult Build(WasmModuleEmissionBuildRequest request);
}
