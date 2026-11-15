// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

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
