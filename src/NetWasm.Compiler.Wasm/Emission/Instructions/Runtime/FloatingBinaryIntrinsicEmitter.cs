using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

// CopySign, Max and Min of two floats or two doubles are one Wasm instruction
// each: f32/f64.max and min are IEEE 754-2019 maximum and minimum, as Math.Max
// and Math.Min are, NaN propagating and -0 below +0.
internal sealed class FloatingBinaryIntrinsicEmitter : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        if (request.Call.Consumed != 2)
        {
            throw new InvalidOperationException($"{request.Intrinsic} requires two floating-point arguments.");
        }

        var kind = request.Instruction.Stack[request.ArgumentBase];
        if (kind is not (CliValueKind.F4 or CliValueKind.F8) ||
            request.Instruction.Stack[request.ArgumentBase + 1] != kind)
        {
            throw new InvalidOperationException($"{request.Intrinsic} requires matching floating-point arguments.");
        }

        var single = kind == CliValueKind.F4;
        var opcode = request.Intrinsic switch
        {
            RuntimeIntrinsic.FloatingCopySign => single ? WasmOpcodes.F32CopySign : WasmOpcodes.F64CopySign,
            RuntimeIntrinsic.FloatingMaximum => single ? WasmOpcodes.F32Maximum : WasmOpcodes.F64Maximum,
            RuntimeIntrinsic.FloatingMinimum => single ? WasmOpcodes.F32Minimum : WasmOpcodes.F64Minimum,
            _ => throw new InvalidOperationException($"{request.Intrinsic} is not a floating-point binary intrinsic."),
        };
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)request.Local(0, kind))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)request.Local(1, kind))));
        code.Write(WasmInstruction.NoOperand(opcode));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)request.Local(0, kind))));
    }
}
