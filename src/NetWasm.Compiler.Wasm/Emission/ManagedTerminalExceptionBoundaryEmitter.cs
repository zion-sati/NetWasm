using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission;

internal sealed class ManagedTerminalExceptionBoundaryEmitter(
    ITargetLayout layouts,
    IRuntimeImportResolver runtimeImports,
    IExceptionPayloadBlockEmitter exceptions,
    IExceptionObjectStateReader exceptionState) : IManagedTerminalExceptionBoundaryEmitter
{
    public void Emit(
        IWasmInstructionWriter code,
        int exceptionLocal,
        int rootFrameLocal,
        int typeIdLocal,
        int messageLocal,
        int messageLengthLocal,
        CliValueKind resultType,
        int resultLocal,
        int reportFunctionIndex,
        Action emitBody)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(emitBody);

        EmitResultBlock(code, resultType);
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
        WriteLocalSet(code, exceptionLocal);

        WriteI32Constant(code, 1);
        WriteCall(code, runtimeImports.Resolve(RuntimeImportSymbol.RootFrameEnter));
        WriteLocalSet(code, rootFrameLocal);
        WriteLocalGet(code, rootFrameLocal);
        WriteLocalGet(code, exceptionLocal);
        ManagedMemoryEmitter.EmitStoreBySize(
            code,
            layouts.Target,
            0,
            layouts.Target.ObjectReferenceSize);

        exceptionState.Emit(
            code,
            exceptionLocal,
            typeIdLocal,
            messageLocal,
            messageLengthLocal);
        WriteLocalGet(code, typeIdLocal);
        WriteLocalGet(code, messageLocal);
        WriteLocalGet(code, messageLengthLocal);
        WriteCall(code, reportFunctionIndex);
        WriteLocalGet(code, rootFrameLocal);
        WriteCall(code, runtimeImports.Resolve(RuntimeImportSymbol.RootFrameLeave));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Unreachable));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitResultBlock(IWasmInstructionWriter code, CliValueKind resultType)
    {
        switch (resultType)
        {
            case CliValueKind.Void:
                WriteBlock(code, WasmOpcodes.EmptyBlockType);
                break;
            case CliValueKind.I4:
                WriteBlock(code, (byte)WasmValueType.I32);
                break;
            case CliValueKind.I8:
                WriteBlock(code, (byte)WasmValueType.I64);
                break;
            case CliValueKind.NativeInt:
            case CliValueKind.ManagedReference:
            case CliValueKind.ManagedAddress:
                WriteBlock(
                    code,
                    layouts.Target.UsesMemory64
                        ? (byte)WasmValueType.I64
                        : (byte)WasmValueType.I32);
                break;
            case CliValueKind.F4:
                WriteBlock(code, (byte)WasmValueType.F32);
                break;
            case CliValueKind.F8:
                WriteBlock(code, (byte)WasmValueType.F64);
                break;
            default:
                throw new InvalidOperationException($"Terminal boundary cannot return {resultType}.");
        }
    }

    private static void WriteBlock(IWasmInstructionWriter code, byte blockType) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Block,
            WasmInstructionOperand.BlockType(blockType)));

    private static void WriteLocalGet(IWasmInstructionWriter code, int index) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)index)));

    private static void WriteLocalSet(IWasmInstructionWriter code, int index) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)index)));

    private static void WriteI32Constant(IWasmInstructionWriter code, int value) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(value)));

    private static void WriteCall(IWasmInstructionWriter code, int index) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)index)));

    private static void WriteBranch(IWasmInstructionWriter code, int depth) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Branch,
            WasmInstructionOperand.Unsigned((uint)depth)));
}
