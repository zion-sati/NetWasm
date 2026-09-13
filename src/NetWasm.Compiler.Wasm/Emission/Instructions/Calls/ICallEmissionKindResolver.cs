using NetWasm.Compiler.Wasm.Encoding;
namespace NetWasm.Compiler.Wasm.Emission.Instructions.Calls;

internal interface ICallEmissionKindResolver
{
    CallEmissionKind Resolve(CallEmissionRequest request, IWasmInstructionWriter code);
}
