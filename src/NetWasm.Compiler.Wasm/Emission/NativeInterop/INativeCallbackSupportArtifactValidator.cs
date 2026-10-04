using NetWasm.Compiler.Wasm.Emission;

namespace NetWasm.Compiler.Wasm.Emission.NativeInterop;

public interface INativeCallbackSupportArtifactValidator
{
    void Validate(WasmNativeCallbackSupportArtifact? support);
}
