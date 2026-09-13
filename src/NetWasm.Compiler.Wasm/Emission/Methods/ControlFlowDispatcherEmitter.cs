using System;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed record ControlFlowDispatcherEmissionRequest(
    StructuredMethod Method,
    StructuredDispatcher Dispatcher,
    int ProgramCounterLocal);

internal sealed class ControlFlowDispatcherEmitter : IControlFlowDispatcherEmitter
{
    public void Emit(
        IWasmInstructionWriter code,
        ControlFlowDispatcherEmissionRequest request,
        Action<StructuredDispatcherBlock> emitBlock,
        Action<StructuredDispatcherBlock> emitCondition,
        Action<StructuredSequence> emitSequence)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Method);
        ArgumentNullException.ThrowIfNull(request.Dispatcher);
        ArgumentNullException.ThrowIfNull(emitBlock);
        ArgumentNullException.ThrowIfNull(emitCondition);
        ArgumentNullException.ThrowIfNull(emitSequence);

        var dispatcher = request.Dispatcher;
        if (dispatcher.EntryBlock is { } entryBlock)
        {
            SetProgramCounter(
                code,
                request.ProgramCounterLocal,
                entryBlock.Value);
        }
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Block,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Loop,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        foreach (var exit in dispatcher.Exits)
        {
            EmitProgramCounterEquals(
                code,
                request.ProgramCounterLocal,
                exit.Target.Value);
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.If,
                WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            if (exit.Body.Regions.IsEmpty)
            {
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.Branch,
                    WasmInstructionOperand.Unsigned(2)));
            }
            else
            {
                emitSequence(exit.Body);
                foreach (var block in dispatcher.Blocks)
                {
                    EmitProgramCounterEquals(
                        code,
                        request.ProgramCounterLocal,
                        block.Occurrence.Block.Value);
                    code.Write(WasmInstruction.WithOperand(
                        WasmOpcodes.BranchIf,
                        WasmInstructionOperand.Unsigned(1)));
                }
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.Branch,
                    WasmInstructionOperand.Unsigned(2)));
            }
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
        foreach (var item in dispatcher.Blocks)
        {
            EmitProgramCounterEquals(
                code,
                request.ProgramCounterLocal,
                item.Occurrence.Block.Value);
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.If,
                WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            if (item.WhenFalse is { } whenFalse)
            {
                emitCondition(item);
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.If,
                    WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
                var whenTrue = item.WhenTrue!.Value;
                SetProgramCounter(
                    code,
                    request.ProgramCounterLocal,
                    whenTrue.Value);
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
                SetProgramCounter(
                    code,
                    request.ProgramCounterLocal,
                    whenFalse.Value);
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
            }
            else
            {
                emitBlock(item);
                if (item.WhenTrue is { } successor)
                {
                    SetProgramCounter(
                        code,
                        request.ProgramCounterLocal,
                        successor.Value);
                }
                else if (request.Method.Blocks[item.Occurrence.Block].Exit is
                         StructuredTerminalExit terminal &&
                         terminal.Instruction.Operation == CilOperation.EndFinally)
                {
                    code.Write(WasmInstruction.WithOperand(
                        WasmOpcodes.Branch,
                        WasmInstructionOperand.Unsigned(2)));
                }
                else
                {
                    code.Write(WasmInstruction.NoOperand(WasmOpcodes.Unreachable));
                }
            }
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Branch,
                WasmInstructionOperand.Unsigned(1)));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Unreachable));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));

    }

    private static void SetProgramCounter(
        IWasmInstructionWriter code,
        int programCounterLocal,
        int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(programCounterLocal);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(value)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)programCounterLocal)));
    }

    private static void EmitProgramCounterEquals(
        IWasmInstructionWriter code,
        int programCounterLocal,
        int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(programCounterLocal);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)programCounterLocal)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(value)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
    }
}
