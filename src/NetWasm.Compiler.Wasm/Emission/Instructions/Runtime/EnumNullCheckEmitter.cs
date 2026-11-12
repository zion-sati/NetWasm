using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class EnumNullCheckEmitter(
    IReferenceComparisonEmitter references,
    IImplicitExceptionEmitter exceptions) : IEnumNullCheckEmitter
{
    public void Emit(
        IWasmInstructionWriter code,
        int objectLocal,
        OptionalFunctionIndex throwFunction,
        ManagedExceptionKind exception = ManagedExceptionKind.NullReference)
    {
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(objectLocal))));
        references.Emit(code, ReferenceComparison.EqualZero);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, exception, throwFunction);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }
}
