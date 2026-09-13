using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class EnumToObjectIntrinsicEmitter(
    IEnumToObjectEmitter emitter) : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code) =>
        emitter.EmitToObject(request, code);
}
