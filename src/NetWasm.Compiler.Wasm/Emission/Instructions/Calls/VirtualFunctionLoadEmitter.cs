using NetWasm.Compiler.Wasm.Encoding;
using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Calls;

internal sealed class VirtualFunctionLoadEmitter(
    ITargetLayout layouts,
    ITypeLayoutProvider typeLayouts,
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
        EmitNullCheck(code, receiverLocal);
        var caller = request.Header.MethodInstance?.CanonicalName ??
                     throw new InvalidOperationException(
                         "a virtual function-load requires a method instance caller");
        var key = $"{caller}@{request.Instruction.Offset:x8}";
        var dispatch = request.Target.DispatchCallSites.TryGetValue(key, out var value)
            ? value
            : throw new InvalidOperationException(
                $"Missing virtual function-load site '{key}'.");
        foreach (var target in dispatch.Targets)
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(receiverLocal))));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load, WasmInstructionOperand.Memory(2, (uint)(0))));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(typeLayouts.GetObjectLayout(target.ReceiverType).TypeId)));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(functionIndices.Resolve(target.Method))));
            if (layouts.Target.UsesMemory64)
            {
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ExtendI32Unsigned));
            }
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(functionLocal))));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        }
        exceptions.Emit(code, ManagedExceptionKind.InvalidCast);
        for (var index = 0; index < dispatch.Targets.Length; index++)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
        request.Stack[receiver] = CliValueKind.NativeInt;
    }

    private void EmitNullCheck(IWasmInstructionWriter code, int receiverLocal)
    {
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(receiverLocal))));
        if (layouts.Target.UsesMemory64) code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64EqualZero));
        else code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.NullReference);
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
