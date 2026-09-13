using NetWasm.Compiler.Wasm.Encoding;
using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class UnsafeAddIntrinsicEmitter(
    ITargetLayout layouts,
    IValueLayoutProvider values) : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        if (request.Method.MethodArguments.Length != 1)
        {
            throw new InvalidOperationException("Unsafe.Add requires one closed type argument");
        }
        var type = request.Method.MethodArguments[0];
        var elementSize = type.IsValueType
            ? values.GetValueLayout(type).Size
            : layouts.Target.GetStorageSize(type);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(request.Local(0, CliValueKind.ManagedAddress)))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(request.Local(1, CliValueKind.I4)))));
        if (layouts.Target.UsesMemory64)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ExtendI32Signed));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I64Constant, WasmInstructionOperand.Signed64(elementSize)));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64Multiply));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64Add));
        }
        else
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(elementSize)));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Multiply));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Add));
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(request.Local(0, CliValueKind.ManagedAddress)))));
    }
}
