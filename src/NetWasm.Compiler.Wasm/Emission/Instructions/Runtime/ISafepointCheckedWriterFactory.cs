using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal interface ISafepointCheckedWriterFactory
{
    // The writer an intrinsic emits its code through.
    IWasmInstructionWriter Create(IWasmInstructionWriter code, RuntimeIntrinsicEmissionRequest request);
}
