// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed record ControlFlowDispatcherEmissionRequest(
    StructuredMethod Method,
    StructuredDispatcher Dispatcher,
    int ProgramCounterLocal);

internal sealed class ControlFlowDispatcherEmitter(
    IDispatcherBranchTablePlanner branchTables) : IControlFlowDispatcherEmitter
{
    private readonly IDispatcherBranchTablePlanner _branchTables = branchTables ??
        throw new ArgumentNullException(nameof(branchTables));

    public void Emit(
        IWasmInstructionWriter code,
        ControlFlowDispatcherEmissionRequest request,
        Action<StructuredDispatcherBlock> emitBlock,
        Action<StructuredDispatcherBlock> emitCondition,
        Action<StructuredSequence, int> emitSequence)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Method);
        ArgumentNullException.ThrowIfNull(request.Dispatcher);
        ArgumentNullException.ThrowIfNull(emitBlock);
        ArgumentNullException.ThrowIfNull(emitCondition);
        ArgumentNullException.ThrowIfNull(emitSequence);
        ArgumentOutOfRangeException.ThrowIfNegative(request.ProgramCounterLocal);

        var dispatcher = request.Dispatcher;
        if (dispatcher.EntryBlock is { } entryBlock)
        {
            SetProgramCounter(
                code,
                request.ProgramCounterLocal,
                entryBlock.Value);
        }

        if (_branchTables.Plan(dispatcher) is { } branchTable)
        {
            EmitBranchTable(
                code,
                request,
                branchTable,
                emitBlock,
                emitCondition,
                emitSequence);
            return;
        }

        EmitLinear(
            code,
            request,
            emitBlock,
            emitCondition,
            emitSequence);
    }

    private static void EmitLinear(
        IWasmInstructionWriter code,
        ControlFlowDispatcherEmissionRequest request,
        Action<StructuredDispatcherBlock> emitBlock,
        Action<StructuredDispatcherBlock> emitCondition,
        Action<StructuredSequence, int> emitSequence)
    {
        var dispatcher = request.Dispatcher;
        // An exit whose body may set the counter to one of the dispatcher's blocks goes on to one shared test of
        // whether it did, after the exits and blocks, rather than a test of its own: each test compares the counter
        // with every block, so a test per exit made the dispatcher's code its exits times its blocks. While that
        // product stays small each exit keeps its own test, which the optimizer folds against the counter the body
        // just set (often to a direct branch, which the shared test, reached from several bodies, cannot be).
        var recheck = SharesRedispatch(dispatcher);
        var shift = recheck ? 1u : 0u;
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Block,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Loop,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        if (recheck)
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Block,
                WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        }
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
                    WasmInstructionOperand.Unsigned(2 + shift)));
            }
            else if (recheck)
            {
                emitSequence(exit.Body, checked((int)(1 + shift)));
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.Branch,
                    WasmInstructionOperand.Unsigned(1)));
            }
            else
            {
                emitSequence(exit.Body, 1);
                EmitRedispatchIfBlock(code, request, 1);
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
                else if (((StructuredTerminalExit)request.Method
                             .Blocks[item.Occurrence.Block].Exit)
                         .Instruction.Operation == CilOperation.EndFinally)
                {
                    code.Write(WasmInstruction.WithOperand(
                        WasmOpcodes.Branch,
                        WasmInstructionOperand.Unsigned(2 + shift)));
                }
                else
                {
                    code.Write(WasmInstruction.NoOperand(WasmOpcodes.Unreachable));
                }
            }
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Branch,
                WasmInstructionOperand.Unsigned(1 + shift)));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Unreachable));
        if (recheck)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
            EmitRedispatchIfBlock(code, request, 0);
        }
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));

    }

    private static void EmitBranchTable(
        IWasmInstructionWriter code,
        ControlFlowDispatcherEmissionRequest request,
        DispatcherBranchTablePlan plan,
        Action<StructuredDispatcherBlock> emitBlock,
        Action<StructuredDispatcherBlock> emitCondition,
        Action<StructuredSequence, int> emitSequence)
    {
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Block,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Loop,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Block,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        foreach (var _ in plan.Cases)
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Block,
                WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        }

        var sharedRedispatch = SharesRedispatch(request.Dispatcher);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)request.ProgramCounterLocal)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.BranchTable,
            WasmInstructionOperand.BranchTable(
                plan.Targets,
                checked((uint)plan.Cases.Length))));

        for (var index = plan.Cases.Length - 1; index >= 0; index--)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
            var loopDepth = checked((uint)(index + 1));
            var dispatcherExitDepth = checked(loopDepth + 1);
            EmitBranchTableCase(
                code,
                request,
                plan.Cases[index],
                loopDepth,
                dispatcherExitDepth,
                sharedRedispatch,
                emitBlock,
                emitCondition,
                emitSequence);
        }

        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Unreachable));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        // The block just inside the loop, which the table's default also ends, is where an exit with a body goes on
        // to the one shared test of whether its body set the counter to one of the dispatcher's blocks. A counter
        // that reaches the default is none of them, as before.
        if (sharedRedispatch)
        {
            EmitRedispatchIfBlock(code, request, 0);
        }
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    // The most compares the exits' own tests may add to a dispatcher (exits with bodies times blocks): above it they
    // share one test, so a dispatcher's code is never its exits times its blocks.
    internal const int MaximumOwnRedispatchCompares = 4096;

    // Whether the exits with bodies share one test of whether a body set the counter to one of the dispatcher's
    // blocks: when their own tests would add more than MaximumOwnRedispatchCompares compares.
    private static bool SharesRedispatch(StructuredDispatcher dispatcher)
    {
        var bodies = 0;
        foreach (var exit in dispatcher.Exits)
        {
            if (!exit.Body.Regions.IsEmpty) bodies++;
        }
        return bodies >= 2 && (long)bodies * dispatcher.Blocks.Length > MaximumOwnRedispatchCompares;
    }

    // Continues the dispatcher's loop, the branch depth given, when the counter names one of its blocks.
    private static void EmitRedispatchIfBlock(
        IWasmInstructionWriter code,
        ControlFlowDispatcherEmissionRequest request,
        uint loopDepth)
    {
        foreach (var block in request.Dispatcher.Blocks)
        {
            EmitProgramCounterEquals(
                code,
                request.ProgramCounterLocal,
                block.Occurrence.Block.Value);
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.BranchIf,
                WasmInstructionOperand.Unsigned(loopDepth)));
        }
    }

    private static void EmitBranchTableCase(
        IWasmInstructionWriter code,
        ControlFlowDispatcherEmissionRequest request,
        DispatcherCasePlan item,
        uint loopDepth,
        uint dispatcherExitDepth,
        bool sharedRedispatch,
        Action<StructuredDispatcherBlock> emitBlock,
        Action<StructuredDispatcherBlock> emitCondition,
        Action<StructuredSequence, int> emitSequence)
    {
        if (item.Exit is { } exit)
        {
            if (!exit.Body.Regions.IsEmpty)
            {
                emitSequence(exit.Body, checked((int)loopDepth));
                if (sharedRedispatch)
                {
                    // To the end of the block just inside the loop, where the shared test follows.
                    code.Write(WasmInstruction.WithOperand(
                        WasmOpcodes.Branch,
                        WasmInstructionOperand.Unsigned(loopDepth - 1)));
                    return;
                }
                EmitRedispatchIfBlock(code, request, loopDepth);
            }
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Branch,
                WasmInstructionOperand.Unsigned(dispatcherExitDepth)));
            return;
        }

        var block = item.Block!;
        if (block.WhenFalse is not null)
        {
            emitCondition(block);
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.If,
                WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            EmitTransition(
                code,
                item.WhenTrue!,
                request.ProgramCounterLocal,
                checked(loopDepth + 1),
                1);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
            EmitTransition(
                code,
                item.WhenFalse!,
                request.ProgramCounterLocal,
                checked(loopDepth + 1),
                1);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
            return;
        }

        emitBlock(block);
        if (item.WhenTrue is { } successor)
        {
            EmitTransition(
                code,
                successor,
                request.ProgramCounterLocal,
                loopDepth,
                0);
            return;
        }
        if (((StructuredTerminalExit)request.Method
                 .Blocks[block.Occurrence.Block].Exit)
             .Instruction.Operation == CilOperation.EndFinally)
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Branch,
                WasmInstructionOperand.Unsigned(dispatcherExitDepth)));
            return;
        }
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Unreachable));
    }

    private static void EmitTransition(
        IWasmInstructionWriter code,
        DispatcherTransitionPlan transition,
        int programCounterLocal,
        uint redispatchDepth,
        uint nestedDepth)
    {
        if (transition.DirectBranchDepth is { } directDepth)
        {
            // An exit leaves the dispatcher with the counter naming its target: a dispatcher around this one, whose
            // exit's body this one is in, dispatches on the counter once this one ends, and must not see the value
            // that brought control into this one.
            if (transition.LeavesDispatcher)
            {
                SetProgramCounter(code, programCounterLocal, transition.Target.Value);
            }
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Branch,
                WasmInstructionOperand.Unsigned(checked(directDepth + nestedDepth))));
            return;
        }
        SetProgramCounter(code, programCounterLocal, transition.Target.Value);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Branch,
            WasmInstructionOperand.Unsigned(redispatchDepth)));
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
