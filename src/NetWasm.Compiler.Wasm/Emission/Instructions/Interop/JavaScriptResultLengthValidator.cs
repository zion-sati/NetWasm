using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

internal sealed class JavaScriptResultLengthValidator(
    IInteropHandleReleaser handles,
    IImplicitExceptionEmitter exceptions) : IJavaScriptResultLengthValidator
{
    public void Validate(
        IWasmInstructionWriter code,
        MethodEmissionContext context,
        InteropMarshallingTarget target)
    {
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(context.InteropResult))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(0)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32LessThanSigned));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        handles.Release(code, context, target);
        exceptions.Emit(code, ManagedExceptionKind.JSException);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }
}
