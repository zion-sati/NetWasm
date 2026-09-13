using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Calls;

internal sealed class ConstrainedReferenceReceiverEmitter(
    ITargetLayout layouts) : IConstrainedReferenceReceiverEmitter
{
    public int? Emit(
        InstructionEmissionRequest instruction,
        IWasmInstructionWriter code,
        int argumentBase,
        CliTypeIdentity? constrainedType)
    {
        if (constrainedType is not { IsValueType: false })
        {
            return null;
        }

        var sourceLocal = WasmLocalLayoutPlanner.GetEvaluationStackLocal(
            instruction.Context.StackLocals,
            argumentBase,
            instruction.Stack[argumentBase],
            layouts.Target);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)sourceLocal)));
        ManagedMemoryEmitter.EmitReferenceLoad(code, layouts.Target, 0);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned(
                (uint)instruction.Context.ObjectTemporary)));
        var receiverLocal = WasmLocalLayoutPlanner.GetEvaluationStackLocal(
            instruction.Context.StackLocals,
            argumentBase,
            CliValueKind.ManagedReference,
            layouts.Target);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(
                (uint)instruction.Context.ObjectTemporary)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)receiverLocal)));
        instruction.Stack[argumentBase] = CliValueKind.ManagedReference;
        return receiverLocal;
    }
}
