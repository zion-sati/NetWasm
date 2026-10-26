using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

// A nullable result arrives as the record JavaScript writes to the descriptor. It
// becomes the struct in the call's value-frame temporary, whose address is the
// stack value, as any value-type call result is.
internal sealed class NullableJavaScriptImportResultMarshaller(
    IRuntimeImportResolver runtimeImports,
    IStackLocalResolver stackLocals,
    IValueFrameAddressEmitter addresses,
    IJavaScriptNullableEmitter nullables) : IJavaScriptImportResultEmitter
{
    public void Emit(JavaScriptImportResultRequest request, IWasmInstructionWriter code)
    {
        var context = request.Context;
        var destination = stackLocals.Resolve(context, request.ArgumentBase, CliValueKind.ValueType);
        addresses.Emit(code, context, context.ValueLayout.TemporaryOffsets[request.Offset]);
        Local(code, WasmOpcodes.LocalSet, destination);
        nullables.EmitFromRecord(
            code,
            request.Signature.ReturnSignatureType,
            () => Local(code, WasmOpcodes.LocalGet, destination),
            () => Local(code, WasmOpcodes.LocalGet, context.InteropDescriptor),
            request.Target.Imports);
        Local(code, WasmOpcodes.LocalGet, context.InteropDescriptor);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned(
            (uint)runtimeImports.Resolve(RuntimeImportSymbol.ValueFrameLeave))));
        request.Stack.RemoveRange(request.ArgumentBase, request.Consumed);
        request.Stack.Add(CliValueKind.ValueType);
    }

    private static void Local(IWasmInstructionWriter code, byte opcode, int local) =>
        code.Write(WasmInstruction.WithOperand(opcode, WasmInstructionOperand.Unsigned((uint)local)));
}
