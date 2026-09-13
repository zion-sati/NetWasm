using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class TypeObjectIdReader(
    IReferenceComparisonEmitter references,
    IImplicitExceptionEmitter exceptions,
    ITargetLayout layouts) : ITypeObjectIdReader
{
    public void Read(IWasmInstructionWriter code, int typeLocal, int typeIdLocal)
    {
        Get(code, typeLocal);
        references.Emit(code, ReferenceComparison.EqualZero);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.ArgumentNull);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));

        Get(code, typeLocal);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Load,
            WasmInstructionOperand.Memory(
                2,
                (uint)layouts.Target.ObjectHeaderSize)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)typeIdLocal)));
    }

    private static void Get(IWasmInstructionWriter code, int local) => code.Write(
        WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)local)));
}
