using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission;

internal sealed class ManagedTerminalTrapBoundaryEmitter(
    ITargetLayout layouts,
    IExceptionPayloadBlockEmitter exceptions) : IManagedTerminalTrapBoundaryEmitter
{
    public void Emit(
        IWasmInstructionWriter code,
        CliValueKind resultType,
        int resultLocal,
        Action emitBody)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(emitBody);

        WriteResultBlock(code, resultType);
        exceptions.Emit(code);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.TryTable,
            WasmInstructionOperand.TryTableCatch(
                WasmOpcodes.EmptyBlockType,
                0,
                0)));
        emitBody();
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        if (resultType != CliValueKind.Void)
            WriteLocalGet(code, resultLocal);
        WriteBranch(code, 1);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Drop));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Unreachable));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void WriteResultBlock(IWasmInstructionWriter code, CliValueKind resultType)
    {
        var blockType = resultType switch
        {
            CliValueKind.Void => WasmOpcodes.EmptyBlockType,
            CliValueKind.I4 => (byte)WasmValueType.I32,
            CliValueKind.I8 => (byte)WasmValueType.I64,
            CliValueKind.NativeInt or CliValueKind.ManagedReference or
                CliValueKind.ManagedAddress => layouts.Target.UsesMemory64
                    ? (byte)WasmValueType.I64
                    : (byte)WasmValueType.I32,
            CliValueKind.F4 => (byte)WasmValueType.F32,
            CliValueKind.F8 => (byte)WasmValueType.F64,
            _ => throw new InvalidOperationException(
                $"Terminal boundary cannot return {resultType}."),
        };
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Block,
            WasmInstructionOperand.BlockType(blockType)));
    }

    private static void WriteLocalGet(IWasmInstructionWriter code, int index) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)index)));

    private static void WriteBranch(IWasmInstructionWriter code, int depth) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Branch,
            WasmInstructionOperand.Unsigned((uint)depth)));
}
