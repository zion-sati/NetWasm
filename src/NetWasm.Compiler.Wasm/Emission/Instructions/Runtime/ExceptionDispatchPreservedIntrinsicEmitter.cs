using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class ExceptionDispatchPreservedIntrinsicEmitter(
    IRuntimeImportResolver runtimeImports) : IRuntimeIntrinsicEmitter
{
    public void Emit(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code)
    {
        var exceptionLocal = request.Local(0, CliValueKind.ManagedReference);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)exceptionLocal)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                RuntimeImportSymbol.BeginRethrow))));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)exceptionLocal)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Throw,
            WasmInstructionOperand.Unsigned(0)));
    }
}
