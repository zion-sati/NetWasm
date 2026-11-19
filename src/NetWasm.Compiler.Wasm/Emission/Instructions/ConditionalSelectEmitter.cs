// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions;

// The compiler's select: the two values and the int32 condition beneath them become Wasm's select, which the engine
// runs without a branch. The values share one numeric kind, so the untyped select applies.
internal sealed class ConditionalSelectEmitter(ITargetLayout layouts) : InstructionCommandProvider
{
    public override ImmutableArray<InstructionCommand> Commands =>
    [
        new(CilOperation.Select, InstructionFamily.Numeric, EmitSelect),
    ];

    private void EmitSelect(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        var whenTrue = request.Stack.Count - 3;
        var type = request.Stack[whenTrue];
        Get(request, code, whenTrue, type);
        Get(request, code, whenTrue + 1, type);
        Get(request, code, whenTrue + 2, CliValueKind.I4);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Select));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)Local(request, whenTrue, type))));
        request.Stack.RemoveRange(whenTrue + 1, 2);
    }

    private void Get(InstructionEmissionRequest request, IWasmInstructionWriter code, int slot, CliValueKind type) =>
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)Local(request, slot, type))));

    private int Local(InstructionEmissionRequest request, int slot, CliValueKind type) =>
        WasmLocalLayoutPlanner.GetEvaluationStackLocal(request.Context.StackLocals, slot, type, layouts.Target);
}
