using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class NativeMemoryReleaseEmitter(
    IRuntimeImportResolver runtimeImports) : INativeMemoryReleaseEmitter
{
    public void Emit(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        RuntimeImportSymbol symbol)
    {
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)request.Local(
                0,
                CliValueKind.NativeInt))));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(symbol))));
    }
}
