// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Wasm.Encoding;
using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Dispatch;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Calls;

// ldvirtftn: the receiver's type id picks the function its type's override compiles to, through the runs of type ids
// that share one, as a virtual call picks its target.
internal sealed class VirtualFunctionLoadEmitter(
    ITargetLayout layouts,
    IVirtualDispatchCasePlanner cases,
    IKeyedDispatchEmitter keyedDispatch,
    IImplicitExceptionEmitter exceptions,
    IInstructionCommandFactory commands) :
    InstructionCommandProvider,
    IVirtualFunctionLoader
{
    public override ImmutableArray<InstructionCommand> Commands =>
    [
        commands.Create(
            CilOperation.LoadVirtualFunction,
            InstructionFamily.CallsAndCallableLoading,
            EmitLoad),
    ];

    public void Load(
        InstructionEmissionRequest request, IWasmInstructionWriter code,
        IFunctionIndexResolver functionIndices) => EmitLoad(request, code, functionIndices);

    private void EmitLoad(
        InstructionEmissionRequest request, IWasmInstructionWriter code,
        IFunctionIndexResolver functionIndices)
    {
        var receiver = request.Stack.Count - 1;
        var receiverLocal = GetStackLocal(
            request.Context,
            receiver,
            CliValueKind.ManagedReference);
        var functionLocal = GetStackLocal(
            request.Context,
            receiver,
            CliValueKind.NativeInt);
        EmitNullCheck(code, receiverLocal, request.Context.ManagedThrowFunction);
        var caller = request.Header.MethodInstance?.CanonicalName ??
                     throw new InvalidOperationException(
                         "a virtual function-load requires a method instance caller");
        var key = $"{caller}@{request.Instruction.Offset:x8}";
        var dispatch = request.Target.DispatchCallSites.TryGetValue(key, out var value)
            ? value
            : throw new InvalidOperationException(
                $"Missing virtual function-load site '{key}'.");
        var dispatchCases = cases.Plan(dispatch.Targets, dispatch.Targets, exhaustive: !dispatch.PreciseTargets);
        var typeIdLocal = request.Context.NumericTemporaryI4;
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)receiverLocal)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load, WasmInstructionOperand.Memory(2, 0)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)typeIdLocal)));
        keyedDispatch.Emit(code, new(
            typeIdLocal,
            wide: false,
            [.. dispatchCases.Select(dispatchCase => new KeyRange(dispatchCase.LowTypeId, dispatchCase.HighTypeId))],
            index =>
            {
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(
                    functionIndices.Resolve(new FunctionAddress(dispatchCases[index].Target.Method)))));
                if (layouts.Target.UsesMemory64)
                {
                    code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ExtendI32Unsigned));
                }
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)functionLocal)));
            },
            () => exceptions.Emit(code, ManagedExceptionKind.InvalidCast, request.Context.ManagedThrowFunction)));
        request.Stack[receiver] = CliValueKind.NativeInt;
    }

    private void EmitNullCheck(
        IWasmInstructionWriter code,
        int receiverLocal,
        OptionalFunctionIndex throwFunction)
    {
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(receiverLocal))));
        if (layouts.Target.UsesMemory64) code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64EqualZero));
        else code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.NullReference, throwFunction);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private int GetStackLocal(
        MethodEmissionContext context,
        int slot,
        CliValueKind type) => WasmLocalLayoutPlanner.GetEvaluationStackLocal(
        context.StackLocals,
        slot,
        type,
        layouts.Target);
}
