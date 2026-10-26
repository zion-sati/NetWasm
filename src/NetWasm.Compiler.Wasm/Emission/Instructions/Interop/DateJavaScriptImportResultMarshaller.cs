using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

// A date result arrives as Unix milliseconds in the descriptor. CoreLib's
// conversion writes the struct to the call's value-frame temporary, whose
// address becomes the stack value, as any value-type call result does.
internal sealed class DateJavaScriptImportResultMarshaller(
    IRuntimeImportResolver runtimeImports,
    IStackLocalResolver stackLocals,
    IValueFrameAddressEmitter addresses) : IJavaScriptImportResultEmitter
{
    public void Emit(JavaScriptImportResultRequest request, IWasmInstructionWriter code)
    {
        var context = request.Context;
        var destination = stackLocals.Resolve(context, request.ArgumentBase, CliValueKind.ValueType);
        addresses.Emit(code, context, context.ValueLayout.TemporaryOffsets[request.Offset]);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalTee, WasmInstructionOperand.Unsigned((uint)destination)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)context.InteropDescriptor)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.F64Load, WasmInstructionOperand.Memory(3, 0)));
        // The descriptor is released before the conversion, which can throw.
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)context.InteropDescriptor)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(RuntimeImportSymbol.ValueFrameLeave))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned(
            (uint)request.Target.Imports.ToManaged(request.Signature.ReturnSignatureType))));
        request.Stack.RemoveRange(request.ArgumentBase, request.Consumed);
        request.Stack.Add(CliValueKind.ValueType);
    }
}
