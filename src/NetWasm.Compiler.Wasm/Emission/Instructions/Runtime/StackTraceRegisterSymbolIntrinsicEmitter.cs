using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

// ProgramSidecars.RegisterMethodSymbol(id, characters, length): the runtime copies a frame's text from the
// method-symbols sidecar CoreLib loaded, for the stack traces it builds.
internal sealed class StackTraceRegisterSymbolIntrinsicEmitter(
    IRuntimeImportResolver runtimeImports) : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)request.Local(0, CliValueKind.I4))));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)request.Local(1, CliValueKind.NativeInt))));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)request.Local(2, CliValueKind.I4))));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned(
                (uint)runtimeImports.Resolve(
                    RuntimeImportSymbol.StackTraceRegisterSymbol,
                    request.Instruction.Target.RuntimeImportSelection))));
    }
}
