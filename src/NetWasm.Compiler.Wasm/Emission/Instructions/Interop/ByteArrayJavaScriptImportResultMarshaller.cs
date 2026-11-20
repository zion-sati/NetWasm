// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

// A number array an import returns arrives staged in linear memory, as a JavaScript value an export or callback takes
// does: the host writes the block's address at the result's offset and the array's length LengthOffset bytes after
// it (-1 for null). Both are read before the call's value frame is left; the array is built from the block, which is
// then freed.
internal sealed class ByteArrayJavaScriptImportResultMarshaller(
    ITargetLayout layouts,
    IRuntimeImportResolver runtimeImports,
    IStackLocalResolver stackLocals,
    IStagedValueArrayEmitter stagedArrays) : IJavaScriptImportResultEmitter
{
    internal const int LengthOffset = sizeof(long);

    public void Emit(JavaScriptImportResultRequest request, IWasmInstructionWriter code)
    {
        var signature = request.Signature;
        var stack = request.Stack;
        var context = request.Context;
        var address = layouts.Target.UsesMemory64 ? context.NumericTemporaryI8 : context.InteropHandle;
        LocalGet(code, context.InteropDescriptor);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load, WasmInstructionOperand.Memory(2, LengthOffset)));
        LocalSet(code, context.InteropResult);
        LocalGet(code, context.InteropDescriptor);
        code.Write(layouts.Target.UsesMemory64
            ? WasmInstruction.WithOperand(WasmOpcodes.I64Load, WasmInstructionOperand.Memory(3, 0))
            : WasmInstruction.WithOperand(WasmOpcodes.I32Load, WasmInstructionOperand.Memory(2, 0)));
        LocalSet(code, address);
        LocalGet(code, context.InteropDescriptor);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(RuntimeImportSymbol.ValueFrameLeave))));
        var destination = stackLocals.Resolve(context, request.ArgumentBase, signature.ReturnType);
        stack.RemoveRange(request.ArgumentBase, request.Consumed);
        stagedArrays.Emit(code, signature.ReturnSignatureType, address, context.InteropResult, destination,
            context.ObjectTemporary, context.ManagedThrowFunction);
        stack.Add(signature.ReturnType);
    }

    private static void LocalGet(IWasmInstructionWriter code, int local) =>
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)local)));

    private static void LocalSet(IWasmInstructionWriter code, int local) =>
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)local)));
}
