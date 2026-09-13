using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Support;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

internal sealed class HostObjectJavaScriptImportResultMarshaller(
    ITargetLayout layouts,
    ITypeLayoutProvider typeLayouts,
    IRuntimeImportResolver runtimeImports,
    IOptionalFunctionIndexValidator functionIndices,
    IJavaScriptResultHandleCapturer resultHandles,
    IStackLocalResolver stackLocals,
    IInteropHandleReleaser interopHandles,
    IAllocationResultValidator allocations,
    IAddressInstructionEmitter addresses) : IJavaScriptImportResultEmitter
{
    public void Emit(JavaScriptImportResultRequest request, IWasmInstructionWriter code)
    {
        var signature = request.Signature;
        var stack = request.Stack;
        var context = request.Context;
        var target = request.Target;
        var callback = request.Callback;
        functionIndices.Validate(
            target.Imports.ReleaseHandle,
            "host object handle support was not emitted");
        resultHandles.Capture(code, context);
        var destination = stackLocals.Resolve(
            context,
            request.ArgumentBase,
            signature.ReturnType);
        stack.RemoveRange(request.ArgumentBase, request.Consumed);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(context.InteropHandle))));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        if (callback is not null)
        {
            interopHandles.Release(code, context);
        }
        addresses.Emit(code, 0);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(destination))));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        var objectLayout = typeLayouts.GetObjectLayout(signature.ReturnSignatureType);
        addresses.Emit(code, objectLayout.Size);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(objectLayout.TypeId)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)(runtimeImports.Resolve(RuntimeImportSymbol.Allocate)))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(context.ObjectTemporary))));
        allocations.Validate(code, context.ObjectTemporary);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(context.ObjectTemporary))));
        addresses.Emit(code, layouts.Target.ObjectHeaderSize);
        addresses.Emit(code, AddressOperation.Add);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(context.InteropHandle))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Store, WasmInstructionOperand.Memory(2, (uint)(0))));
        if (callback is not null &&
            InteropTypeClassifier.IsSubscription(signature.ReturnSignatureType))
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(context.ObjectTemporary))));
            addresses.Emit(
                code,
                layouts.Target.ObjectHeaderSize + sizeof(int));
            addresses.Emit(code, AddressOperation.Add);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(context.InteropResult))));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Store, WasmInstructionOperand.Memory(2, (uint)(0))));
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(context.ObjectTemporary))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(destination))));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        stack.Add(signature.ReturnType);
    }
}
