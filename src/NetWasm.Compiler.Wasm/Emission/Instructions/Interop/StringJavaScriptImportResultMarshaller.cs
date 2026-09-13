using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Support;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

internal sealed class StringJavaScriptImportResultMarshaller(
    ITypeLayoutProvider layouts,
    IRuntimeImportResolver runtimeImports,
    IImplicitExceptionEmitter exceptions,
    IOptionalFunctionIndexValidator functionIndices,
    IJavaScriptResultHandleCapturer resultHandles,
    IStackLocalResolver stackLocals,
    IJavaScriptResultLengthValidator resultLengths,
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
        functionIndices.Validate(
            target.Imports.StringLength,
            "string host result helpers were not emitted");
        functionIndices.Validate(
            target.Imports.CopyStringUtf16,
            "string host result helpers were not emitted");
        functionIndices.Validate(
            target.Imports.ReleaseHandle,
            "string host result helpers were not emitted");
        resultHandles.Capture(code, context);
        var destination = stackLocals.Resolve(
            context,
            request.ArgumentBase,
            signature.ReturnType);
        stack.RemoveRange(request.ArgumentBase, request.Consumed);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(context.InteropHandle))));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        addresses.Emit(code, 0);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(destination))));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(context.InteropHandle))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)(target.Imports.StringLength.Value))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(context.InteropResult))));
        resultLengths.Validate(code, context, target);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(context.InteropResult))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed((int)((uint.MaxValue - 2 * sizeof(int)) / sizeof(char)))));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32GreaterThanUnsigned));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        interopHandles.Release(code, context, target);
        exceptions.Emit(code, ManagedExceptionKind.OutOfMemory);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(0)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(context.InteropResult))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(layouts.StringTypeId)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)(runtimeImports.Resolve(RuntimeImportSymbol.AllocateString)))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(context.ObjectTemporary))));
        allocations.Validate(code, context.ObjectTemporary);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(context.InteropHandle))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(context.ObjectTemporary))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)(target.Imports.CopyStringUtf16.Value))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        interopHandles.Release(code, context, target);
        exceptions.Emit(code, ManagedExceptionKind.JSException);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        interopHandles.Release(code, context, target);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(context.ObjectTemporary))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(destination))));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        stack.Add(signature.ReturnType);
    }
}
