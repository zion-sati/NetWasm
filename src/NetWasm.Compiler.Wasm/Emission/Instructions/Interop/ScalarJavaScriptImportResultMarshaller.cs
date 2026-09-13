using NetWasm.Compiler.Wasm.Encoding;
using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

internal sealed class ScalarJavaScriptImportResultMarshaller(
    ITargetLayout layouts,
    IRuntimeImportResolver runtimeImports,
    IStackLocalResolver stackLocals) : IJavaScriptImportResultEmitter
{
    public void Emit(JavaScriptImportResultRequest request, IWasmInstructionWriter code)
    {
        var signature = request.Signature;
        var stack = request.Stack;
        var context = request.Context;
        var destination = signature.ReturnType == CliValueKind.Void
            ? -1
            : stackLocals.Resolve(context, request.ArgumentBase, signature.ReturnType);
        if (destination >= 0)
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(context.InteropDescriptor))));
            EmitScalarResultLoad(code, signature.ReturnType);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(destination))));
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(context.InteropDescriptor))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)(runtimeImports.Resolve(RuntimeImportSymbol.ValueFrameLeave)))));
        stack.RemoveRange(request.ArgumentBase, request.Consumed);
        if (signature.ReturnType != CliValueKind.Void)
        {
            stack.Add(signature.ReturnType);
        }
    }

    private void EmitScalarResultLoad(IWasmInstructionWriter code, CliValueKind type)
    {
        switch (type)
        {
            case CliValueKind.I4: code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load, WasmInstructionOperand.Memory(2, (uint)(0)))); break;
            case CliValueKind.I8: code.Write(WasmInstruction.WithOperand(WasmOpcodes.I64Load, WasmInstructionOperand.Memory(3, (uint)(0)))); break;
            case CliValueKind.F4: code.Write(WasmInstruction.WithOperand(WasmOpcodes.F32Load, WasmInstructionOperand.Memory(2, (uint)(0)))); break;
            case CliValueKind.F8: code.Write(WasmInstruction.WithOperand(WasmOpcodes.F64Load, WasmInstructionOperand.Memory(3, (uint)(0)))); break;
            case CliValueKind.NativeInt when layouts.Target.UsesMemory64:
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.I64Load, WasmInstructionOperand.Memory(3, (uint)(0))));
                break;
            case CliValueKind.NativeInt: code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load, WasmInstructionOperand.Memory(2, (uint)(0)))); break;
            default:
                throw new InvalidOperationException("unsupported scalar host result type");
        }
    }
}
