using NetWasm.Compiler.Wasm.Encoding;
using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Memory;

internal sealed class AtomicInstructionEmitter(
    ITargetLayout layouts,
    ICilTypeOperandResolver types) : InstructionCommandProvider
{
    public override ImmutableArray<InstructionCommand> Commands =>
    [
        new(
            CilOperation.CompareExchange,
            InstructionFamily.ArraysFieldsStatics,
            EmitCompareExchange),
    ];

    private void EmitCompareExchange(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        var address = request.Stack.Count - 3;
        var resultKind = types.Resolve(request.Instruction, request.Header.MethodInstance).StackKind;
        var encoding = SelectEncoding(resultKind);
        var temporary = encoding.UsesI64
            ? request.Context.NumericTemporaryI8
            : request.Context.NumericTemporaryI4;
        var addressLocal = GetStackLocal(
            request.Context,
            address,
            request.Stack[address]);
        var valueLocal = GetStackLocal(
            request.Context,
            address + 1,
            request.Stack[address + 1]);
        var comparandLocal = GetStackLocal(
            request.Context,
            address + 2,
            request.Stack[address + 2]);
        var resultLocal = GetStackLocal(
            request.Context,
            address,
            resultKind);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(addressLocal))));
        code.Write(WasmInstruction.WithOperand(
            encoding.Load,
            WasmInstructionOperand.Memory(encoding.Alignment, 0)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)temporary)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)temporary)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(comparandLocal))));
        code.Write(WasmInstruction.NoOperand(encoding.Equal));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(addressLocal))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(valueLocal))));
        code.Write(WasmInstruction.WithOperand(
            encoding.Store,
            WasmInstructionOperand.Memory(encoding.Alignment, 0)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)temporary)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(resultLocal))));
        request.Stack.RemoveRange(address + 1, 2);
        request.Stack[address] = resultKind;
    }

    private AtomicValueEncoding SelectEncoding(CliValueKind kind)
    {
        var usesI64 = kind == CliValueKind.I8 ||
            (layouts.Target.UsesMemory64 &&
                kind is CliValueKind.NativeInt or CliValueKind.ManagedReference or
                    CliValueKind.ManagedAddress);
        if (kind is not (CliValueKind.I4 or CliValueKind.I8 or
            CliValueKind.NativeInt or CliValueKind.ManagedReference or
            CliValueKind.ManagedAddress))
        {
            throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.UnsupportedCil,
                $"compare-exchange value kind '{kind}' is not supported"));
        }
        return usesI64
            ? new(
                WasmOpcodes.I64Load,
                WasmOpcodes.I64Store,
                WasmOpcodes.I64Equal,
                3,
                UsesI64: true)
            : new(
                WasmOpcodes.I32Load,
                WasmOpcodes.I32Store,
                WasmOpcodes.I32Equal,
                2,
                UsesI64: false);
    }

    private readonly record struct AtomicValueEncoding(
        byte Load,
        byte Store,
        byte Equal,
        uint Alignment,
        bool UsesI64);

    private int GetStackLocal(
        MethodEmissionContext context,
        int slot,
        CliValueKind type) => WasmLocalLayoutPlanner.GetEvaluationStackLocal(
        context.StackLocals,
        slot,
        type,
        layouts.Target);
}
