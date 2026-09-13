using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed class StructuredControlFlowEmitter(
    ITargetLayout layouts,
    IControlFlowDispatcherEmitter dispatchers,
    ILogger<WasmModuleEmitterFactory>? diagnosticLogger = null) :
    IStructuredControlFlowEmitter
{
    private static readonly Action<ILogger, int, string, int, int, int, Exception?> LogSequence =
        LoggerMessage.Define<int, string, int, int, int>(
            LogLevel.Trace,
            new EventId(4310, nameof(LogSequence)),
            "Structured sequence emission regions {RegionCount}, first {FirstRegion}, break depth {LoopBreakDepth}, continue depth {LoopContinueDepth}, dispatcher depth {DispatcherContinueDepth}.");

    private static readonly Action<ILogger, string, Exception?> LogInvalidMarker =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(4311, nameof(LogInvalidMarker)),
            "Structured control marker has no active target {MarkerKind}.");

    private readonly ILogger<WasmModuleEmitterFactory> _diagnosticLogger =
        diagnosticLogger ?? NullLogger<WasmModuleEmitterFactory>.Instance;

    public void Emit(
        IWasmInstructionWriter code,
        StructuredMethod method,
        StructuredSequence sequence,
        MethodEmissionContext context,
        Func<StructuredBlockOccurrence, bool, bool, ConditionValue> emitBlock,
        Action<StructuredExceptionRegion, MethodEmissionContext, bool, int?, int?, int?>
            emitExceptionRegion,
        int? loopBreakDepth = null,
        int? loopContinueDepth = null,
        int? exceptionLeaveDepth = null,
        int? dispatcherContinueDepth = null)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(sequence);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(emitBlock);
        ArgumentNullException.ThrowIfNull(emitExceptionRegion);

        LogSequence(
            _diagnosticLogger,
            sequence.Regions.Length,
            sequence.Regions.IsEmpty
                ? "empty"
                : sequence.Regions[0].GetType().Name,
            loopBreakDepth ?? -1,
            loopContinueDepth ?? -1,
            dispatcherContinueDepth ?? -1,
            null);

        foreach (var region in sequence.Regions)
        {
            switch (region)
            {
                case StructuredCode block:
                    emitBlock(
                        block.Occurrence,
                        false,
                        block.Occurrence.Role == StructuredBlockRole.Owner);
                    break;
                case StructuredLoopBreak:
                    if (loopBreakDepth is null)
                    {
                        LogInvalidMarker(_diagnosticLogger, nameof(StructuredLoopBreak), null);
                    }
                    code.Write(WasmInstruction.WithOperand(
                        WasmOpcodes.Branch,
                        WasmInstructionOperand.Unsigned((uint)(loopBreakDepth ?? throw new InvalidOperationException(
                            "structured loop break was emitted outside a loop")))));
                    break;
                case StructuredLoopContinue:
                    if (loopContinueDepth is null)
                    {
                        LogInvalidMarker(_diagnosticLogger, nameof(StructuredLoopContinue), null);
                    }
                    code.Write(WasmInstruction.WithOperand(
                        WasmOpcodes.Branch,
                        WasmInstructionOperand.Unsigned((uint)(loopContinueDepth ?? throw new InvalidOperationException(
                            "structured loop continue was emitted outside a loop")))));
                    break;
                case StructuredDispatcherContinue dispatcherContinue:
                    if (dispatcherContinueDepth is null)
                    {
                        LogInvalidMarker(
                            _diagnosticLogger,
                            nameof(StructuredDispatcherContinue),
                            null);
                    }
                    code.Write(WasmInstruction.WithOperand(
                        WasmOpcodes.I32Constant,
                        WasmInstructionOperand.Signed(dispatcherContinue.Target.Value)));
                    code.Write(WasmInstruction.WithOperand(
                        WasmOpcodes.LocalSet,
                        WasmInstructionOperand.Unsigned((uint)context.DispatcherProgramCounter)));
                    code.Write(WasmInstruction.WithOperand(
                        WasmOpcodes.Branch,
                        WasmInstructionOperand.Unsigned((uint)(dispatcherContinueDepth ??
                            throw new InvalidOperationException(
                                "structured dispatcher continuation was emitted outside a dispatcher")))));
                    break;
                case StructuredExceptionRegion exception:
                    emitExceptionRegion(
                        exception,
                        context,
                        context.ActiveExceptionGroup is null,
                        loopBreakDepth,
                        loopContinueDepth,
                        dispatcherContinueDepth);
                    break;
                case StructuredDispatcher dispatcher:
                    dispatchers.Emit(
                        code,
                        new ControlFlowDispatcherEmissionRequest(
                            method,
                            dispatcher,
                            context.DispatcherProgramCounter),
                        block => emitBlock(
                            block.Occurrence,
                            false,
                            block.Occurrence.Role == StructuredBlockRole.Owner),
                        block => EmitConditionValue(
                            code,
                            context,
                            emitBlock(
                                block.Occurrence,
                                true,
                                block.Occurrence.Role == StructuredBlockRole.Owner)),
                        body => Emit(
                            code,
                            method,
                            body,
                            context,
                            emitBlock,
                            emitExceptionRegion,
                            loopBreakDepth: loopBreakDepth is int dispatcherBreakDepth
                                ? dispatcherBreakDepth + 3
                                : null,
                            loopContinueDepth:
                                loopContinueDepth is int dispatcherContinueLoopDepth
                                    ? dispatcherContinueLoopDepth + 3
                                    : null,
                            exceptionLeaveDepth:
                                exceptionLeaveDepth is int dispatcherLeaveDepth
                                    ? dispatcherLeaveDepth + 3
                                    : null,
                            dispatcherContinueDepth: 1));
                    break;
                case StructuredIf conditional:
                    EmitConditional(
                        code,
                        conditional,
                        method,
                        context,
                        emitBlock,
                        emitExceptionRegion,
                        loopBreakDepth,
                        loopContinueDepth,
                        exceptionLeaveDepth,
                        dispatcherContinueDepth);
                    break;
                case StructuredLoop loop:
                    EmitLoop(
                        code,
                        loop,
                        method,
                        context,
                        emitBlock,
                        emitExceptionRegion,
                        loopBreakDepth,
                        loopContinueDepth,
                        exceptionLeaveDepth,
                        dispatcherContinueDepth);
                    break;
                case StructuredPostTestLoop loop:
                    EmitPostTestLoop(
                        code,
                        loop,
                        method,
                        context,
                        emitBlock,
                        emitExceptionRegion,
                        loopBreakDepth,
                        loopContinueDepth,
                        exceptionLeaveDepth,
                        dispatcherContinueDepth);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unknown structured region {region.GetType().Name}.");
            }
            if (exceptionLeaveDepth is int leaveDepth &&
                context.ActiveExceptionGroup is { } activeGroup)
            {
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.LocalGet,
                    WasmInstructionOperand.Unsigned((uint)context.ExceptionContinuationLocals[activeGroup.Id])));
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.BranchIf,
                    WasmInstructionOperand.Unsigned((uint)leaveDepth)));
            }
        }
    }

    private void EmitConditional(
        IWasmInstructionWriter code,
        StructuredIf conditional,
        StructuredMethod method,
        MethodEmissionContext context,
        Func<StructuredBlockOccurrence, bool, bool, ConditionValue> emitBlock,
        Action<StructuredExceptionRegion, MethodEmissionContext, bool, int?, int?, int?>
            emitExceptionRegion,
        int? loopBreakDepth,
        int? loopContinueDepth,
        int? exceptionLeaveDepth,
        int? dispatcherContinueDepth)
    {
        EmitConditionValue(
            code,
            context,
            emitBlock(
                conditional.Condition,
                true,
                conditional.Condition.Role == StructuredBlockRole.Owner));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        Emit(
            code,
            method,
            conditional.WhenTrue,
            context,
            emitBlock,
            emitExceptionRegion,
            loopBreakDepth is int trueDepth ? trueDepth + 1 : null,
            loopContinueDepth is int trueContinueDepth
                ? trueContinueDepth + 1
                : null,
            exceptionLeaveDepth is int trueLeaveDepth ? trueLeaveDepth + 1 : null,
            dispatcherContinueDepth is int trueDispatcherDepth
                ? trueDispatcherDepth + 1
                : null);
        if (!conditional.WhenFalse.Regions.IsEmpty)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
            Emit(
                code,
                method,
                conditional.WhenFalse,
                context,
                emitBlock,
                emitExceptionRegion,
                loopBreakDepth is int falseDepth ? falseDepth + 1 : null,
                loopContinueDepth is int falseContinueDepth
                    ? falseContinueDepth + 1
                    : null,
                exceptionLeaveDepth is int falseLeaveDepth ? falseLeaveDepth + 1 : null,
                dispatcherContinueDepth is int falseDispatcherDepth
                    ? falseDispatcherDepth + 1
                    : null);
        }
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitLoop(
        IWasmInstructionWriter code,
        StructuredLoop loop,
        StructuredMethod method,
        MethodEmissionContext context,
        Func<StructuredBlockOccurrence, bool, bool, ConditionValue> emitBlock,
        Action<StructuredExceptionRegion, MethodEmissionContext, bool, int?, int?, int?>
            emitExceptionRegion,
        int? outerLoopBreakDepth,
        int? outerLoopContinueDepth,
        int? exceptionLeaveDepth,
        int? dispatcherContinueDepth)
    {
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Block,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Block,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Loop,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        EmitConditionValue(
            code,
            context,
            emitBlock(
                loop.Condition,
                true,
                loop.Condition.Role == StructuredBlockRole.Owner));
        if (loop.ContinueWhenConditionTrue)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        }
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.BranchIf,
            WasmInstructionOperand.Unsigned(1)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Block,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        Emit(
            code,
            method,
            loop.Body,
            context,
            emitBlock,
            emitExceptionRegion,
            3,
            0,
            exceptionLeaveDepth is int leaveDepth ? leaveDepth + 4 : null,
            dispatcherContinueDepth is int bodyDispatcherDepth
                ? bodyDispatcherDepth + 4
                : null);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        Emit(
            code,
            method,
            loop.ContinueBody,
            context,
            emitBlock,
            emitExceptionRegion,
            2,
            0,
            exceptionLeaveDepth is int continueLeaveDepth
                ? continueLeaveDepth + 3
                : null,
            dispatcherContinueDepth is int continueDispatcherDepth
                ? continueDispatcherDepth + 3
                : null);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Branch,
            WasmInstructionOperand.Unsigned(0)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        Emit(
            code,
            method,
            loop.ExitBody,
            context,
            emitBlock,
            emitExceptionRegion,
            loopBreakDepth: outerLoopBreakDepth is int exitBreakDepth
                ? exitBreakDepth + 1
                : null,
            loopContinueDepth: outerLoopContinueDepth is int exitContinueDepth
                ? exitContinueDepth + 1
                : null,
            exceptionLeaveDepth: exceptionLeaveDepth is int exitLeaveDepth
                ? exitLeaveDepth + 1
                : null,
            dispatcherContinueDepth: dispatcherContinueDepth is int exitDispatcherDepth
                ? exitDispatcherDepth + 1
                : null);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitPostTestLoop(
        IWasmInstructionWriter code,
        StructuredPostTestLoop loop,
        StructuredMethod method,
        MethodEmissionContext context,
        Func<StructuredBlockOccurrence, bool, bool, ConditionValue> emitBlock,
        Action<StructuredExceptionRegion, MethodEmissionContext, bool, int?, int?, int?>
            emitExceptionRegion,
        int? outerLoopBreakDepth,
        int? outerLoopContinueDepth,
        int? exceptionLeaveDepth,
        int? dispatcherContinueDepth)
    {
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Block,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Block,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Loop,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Block,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        Emit(
            code,
            method,
            loop.Body,
            context,
            emitBlock,
            emitExceptionRegion,
            3,
            0,
            exceptionLeaveDepth is int bodyLeaveDepth ? bodyLeaveDepth + 4 : null,
            dispatcherContinueDepth is int bodyDispatcherDepth
                ? bodyDispatcherDepth + 4
                : null);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        EmitConditionValue(
            code,
            context,
            emitBlock(
                loop.Condition,
                true,
                loop.Condition.Role == StructuredBlockRole.Owner));
        if (!loop.ContinueWhenConditionTrue)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        }
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        Emit(
            code,
            method,
            loop.ContinueBody,
            context,
            emitBlock,
            emitExceptionRegion,
            exceptionLeaveDepth: exceptionLeaveDepth is int continueLeaveDepth
                ? continueLeaveDepth + 4
                : null,
            dispatcherContinueDepth: dispatcherContinueDepth is int continueDispatcherDepth
                ? continueDispatcherDepth + 4
                : null);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Branch,
            WasmInstructionOperand.Unsigned(1)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        Emit(
            code,
            method,
            loop.ExitBody,
            context,
            emitBlock,
            emitExceptionRegion,
            loopBreakDepth: outerLoopBreakDepth is int exitBreakDepth
                ? exitBreakDepth + 1
                : null,
            loopContinueDepth: outerLoopContinueDepth is int exitContinueDepth
                ? exitContinueDepth + 1
                : null,
            exceptionLeaveDepth: exceptionLeaveDepth is int exitLeaveDepth
                ? exitLeaveDepth + 1
                : null,
            dispatcherContinueDepth: dispatcherContinueDepth is int exitDispatcherDepth
                ? exitDispatcherDepth + 1
                : null);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitConditionValue(
        IWasmInstructionWriter code,
        MethodEmissionContext context,
        ConditionValue condition)
    {
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)WasmLocalLayoutPlanner.GetEvaluationStackLocal(
                context.StackLocals,
                condition.Slot,
                condition.Type,
                layouts.Target))));
        if (condition.Type is CliValueKind.ManagedReference or CliValueKind.ManagedAddress)
        {
            if (layouts.Target.UsesMemory64)
            {
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64EqualZero));
            }
            else
            {
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
            }
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        }
        else if (condition.Type == CliValueKind.I8 ||
                 condition.Type == CliValueKind.NativeInt && layouts.Target.UsesMemory64)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64EqualZero));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        }
    }
}
