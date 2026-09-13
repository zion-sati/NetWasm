namespace NetWasm.Compiler.Wasm.ModuleEncoding;

public interface IModuleEncoder
{
    byte[] Encode(WasmModuleBuildRequest request);
}
