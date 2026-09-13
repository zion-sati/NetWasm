using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.NativeIntegers;

internal interface INativeIntegerConversionEmitter
{
    void Emit(IWasmInstructionWriter code, CliValueKind source, bool unsigned);
}
