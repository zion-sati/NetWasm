namespace NetWasm.Compiler.Wasm.Units;

internal interface IWasmIndexRemapping
{
    uint Remap(WasmIndexSpace space, uint index);
}
