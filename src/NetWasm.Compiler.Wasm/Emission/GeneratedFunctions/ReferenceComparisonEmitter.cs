using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class ReferenceComparisonEmitter(ITargetLayout layouts) :
    IReferenceComparisonEmitter
{
    public void Emit(
        IWasmInstructionWriter code,
        ReferenceComparison comparison,
        CliValueKind operandType = CliValueKind.ManagedReference)
    {
        var valueType = WasmValueTypes.FromCli(operandType, layouts.Target);
        var opcode = (comparison, valueType) switch
        {
            (ReferenceComparison.Equal, WasmValueType.I32) => WasmOpcodes.I32Equal,
            (ReferenceComparison.Equal, WasmValueType.I64) => WasmOpcodes.I64Equal,
            (ReferenceComparison.EqualZero, WasmValueType.I32) => WasmOpcodes.I32EqualZero,
            (ReferenceComparison.EqualZero, WasmValueType.I64) => WasmOpcodes.I64EqualZero,
            _ => throw new ArgumentOutOfRangeException(nameof(comparison), comparison,
                $"'{operandType}' is not a supported integer reference-comparison operand."),
        };
        code.Write(WasmInstruction.NoOperand(opcode));
    }
}
