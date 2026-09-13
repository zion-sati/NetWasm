using NetWasm.Compiler.Wasm.Encoding;
using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;

namespace NetWasm.Compiler.Wasm.Emission.Instructions;

internal sealed class ConstantsStackEmitter(
    ITargetLayout layouts,
    ITypeLayoutProvider typeLayouts,
    IStaticDataLayout staticData,
    ICilTypeOperandResolver types) : InstructionCommandProvider
{
    public override ImmutableArray<InstructionCommand> Commands =>
    [
        Command(CilOperation.Nop, EmitNop),
        Command(CilOperation.Break, EmitNop),
        Command(CilOperation.LoadInt32, EmitInt32),
        Command(CilOperation.LoadInt64, EmitInt64),
        Command(CilOperation.LoadFloat32, EmitFloat32),
        Command(CilOperation.LoadFloat64, EmitFloat64),
        Command(CilOperation.LoadNull, EmitNull),
        Command(CilOperation.LoadString, EmitString),
        Command(CilOperation.LoadTypeToken, EmitTypeToken),
        Command(CilOperation.LoadFieldToken, EmitFieldToken),
        Command(CilOperation.Duplicate, EmitDuplicate),
        Command(CilOperation.Pop, EmitPop),
    ];

    private static InstructionCommand Command(
        CilOperation operation,
        Action<InstructionEmissionRequest, IWasmInstructionWriter> emit) => new(
        operation,
        InstructionFamily.ConstantsStackLocalsArguments,
        emit);

    private static void EmitNop(InstructionEmissionRequest request, IWasmInstructionWriter code) =>
        ArgumentNullException.ThrowIfNull(request);

    private void EmitInt32(InstructionEmissionRequest request, IWasmInstructionWriter code) => EmitConstant(
        request, code, ((CilOperand.ConstantI4)request.Instruction.Operand).Value);

    private void EmitInt64(InstructionEmissionRequest request, IWasmInstructionWriter code) => EmitConstant(
        request, code, ((CilOperand.ConstantI8)request.Instruction.Operand).Value);

    private void EmitFloat32(InstructionEmissionRequest request, IWasmInstructionWriter code) => EmitConstant(
        request, code, ((CilOperand.ConstantF4)request.Instruction.Operand).Value);

    private void EmitFloat64(InstructionEmissionRequest request, IWasmInstructionWriter code) => EmitConstant(
        request, code, ((CilOperand.ConstantF8)request.Instruction.Operand).Value);

    private void EmitNull(InstructionEmissionRequest request, IWasmInstructionWriter code) =>
        EmitConstant(request, code, 0, CliValueKind.ManagedReference);

    private void EmitString(InstructionEmissionRequest request, IWasmInstructionWriter code) => EmitConstant(
        request, code, staticData.GetStringLayout(
            ((CilOperand.UserString)request.Instruction.Operand).Value).Address,
        CliValueKind.ManagedReference);

    private void EmitTypeToken(InstructionEmissionRequest request, IWasmInstructionWriter code) => EmitConstant(
        request, code, typeLayouts.GetObjectLayout(types.Resolve(request.Instruction, request.Header.MethodInstance)).TypeId);

    private void EmitFieldToken(InstructionEmissionRequest request, IWasmInstructionWriter code) => EmitConstant(
        request, code, ((CilOperand.Entity)request.Instruction.Operand).Key.MetadataToken);

    private void EmitDuplicate(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        var type = request.Stack[^1];
        var source = GetStackLocal(request.Context, request.Stack.Count - 1, type);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(source))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
            request.Context,
            request.Stack.Count,
            type)))));
        request.Stack.Add(type);
    }

    private static void EmitPop(InstructionEmissionRequest request, IWasmInstructionWriter code) =>
        request.Stack.RemoveAt(request.Stack.Count - 1);

    private void EmitConstant(
        InstructionEmissionRequest request, IWasmInstructionWriter code,
        int value,
        CliValueKind type = CliValueKind.I4)
    {
        if (layouts.Target.UsesMemory64 &&
            type is CliValueKind.ManagedReference or CliValueKind.ManagedAddress)
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I64Constant, WasmInstructionOperand.Signed64(value)));
        }
        else
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(value)));
        }
        Store(request, code, type);
    }

    private void EmitConstant(InstructionEmissionRequest request, IWasmInstructionWriter code, long value)
    {
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I64Constant, WasmInstructionOperand.Signed64(value)));
        Store(request, code, CliValueKind.I8);
    }

    private void EmitConstant(InstructionEmissionRequest request, IWasmInstructionWriter code, float value)
    {
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.F32Constant, WasmInstructionOperand.Float32(value)));
        Store(request, code, CliValueKind.F4);
    }

    private void EmitConstant(InstructionEmissionRequest request, IWasmInstructionWriter code, double value)
    {
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.F64Constant, WasmInstructionOperand.Float64(value)));
        Store(request, code, CliValueKind.F8);
    }

    private void Store(InstructionEmissionRequest request, IWasmInstructionWriter code, CliValueKind type)
    {
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
            request.Context,
            request.Stack.Count,
            type)))));
        request.Stack.Add(type);
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
