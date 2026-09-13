using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Calls;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed record RuntimeIntrinsicEmissionRequest(
    CallEmissionRequest Call,
    RuntimeIntrinsic Intrinsic,
    CliTypeIdentity? ConstrainedType,
    WasmTargetLayout Target,
    IFunctionIndexResolver FunctionIndices)
{
    public InstructionEmissionRequest Instruction => Call.Instruction;
    public MethodInstanceModel Method => Call.Method;
    public int ArgumentBase => Call.ArgumentBase;

    public int Local(int relativeSlot, CliValueKind type) =>
        WasmLocalLayoutPlanner.GetEvaluationStackLocal(
            Instruction.Context.StackLocals,
            ArgumentBase + relativeSlot,
            type,
            Target);
}
