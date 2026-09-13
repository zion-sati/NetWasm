using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class UnsafeSizeOfIntrinsicEmitter(
    IValueLayoutProvider values) : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        if (request.Method.MethodArguments.Length != 1)
        {
            throw new InvalidOperationException("Unsafe.SizeOf requires one closed type argument");
        }

        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(
                values.GetValueLayout(request.Method.MethodArguments[0]).Size)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)request.Local(0, CliValueKind.I4))));
    }
}
