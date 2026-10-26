using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

// An object, exception or array of references arrives as a handle to its
// JavaScript value in the descriptor. CoreLib converts the value, and the handle
// is released.
internal sealed class HandleValueJavaScriptImportResultMarshaller(
    IOptionalFunctionIndexValidator functionIndices,
    IJavaScriptResultHandleCapturer resultHandles,
    IStackLocalResolver stackLocals) : IJavaScriptImportResultEmitter
{
    public void Emit(JavaScriptImportResultRequest request, IWasmInstructionWriter code)
    {
        var context = request.Context;
        var imports = request.Target.Imports;
        functionIndices.Validate(imports.ReleaseHandle, "host handle support was not emitted");
        resultHandles.Capture(code, context);
        var destination = stackLocals.Resolve(context, request.ArgumentBase, CliValueKind.ManagedReference);
        Local(code, WasmOpcodes.LocalGet, context.InteropHandle);
        Call(code, imports.ToManaged(request.Signature.ReturnSignatureType));
        Local(code, WasmOpcodes.LocalSet, destination);
        Local(code, WasmOpcodes.LocalGet, context.InteropHandle);
        Call(code, imports.ReleaseHandle.Value);
        request.Stack.RemoveRange(request.ArgumentBase, request.Consumed);
        request.Stack.Add(CliValueKind.ManagedReference);
    }

    private static void Local(IWasmInstructionWriter code, byte opcode, int local) =>
        code.Write(WasmInstruction.WithOperand(opcode, WasmInstructionOperand.Unsigned((uint)local)));

    private static void Call(IWasmInstructionWriter code, int function) =>
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)function)));
}
