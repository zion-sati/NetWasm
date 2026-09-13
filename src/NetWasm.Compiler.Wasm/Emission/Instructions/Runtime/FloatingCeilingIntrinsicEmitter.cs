using NetWasm.Compiler.Wasm.Encoding;
using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class FloatingCeilingIntrinsicEmitter : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code) =>
        EmitUnary(request, code, static code => code.Write(WasmInstruction.NoOperand(WasmOpcodes.F32Ceiling)), static code => code.Write(WasmInstruction.NoOperand(WasmOpcodes.F64Ceiling)));

    private static void EmitUnary(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code, Action<IWasmInstructionWriter> emitF32, Action<IWasmInstructionWriter> emitF64)
    {
        var type = request.Instruction.Stack[request.ArgumentBase];
        if (type is not (CliValueKind.F4 or CliValueKind.F8)) throw new InvalidOperationException($"{request.Intrinsic} requires a floating-point argument");
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(request.Local(0, type)))));
        if (type == CliValueKind.F4) emitF32(code); else emitF64(code);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(request.Local(0, type)))));
    }
}
