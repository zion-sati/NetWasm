using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Arrays;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed class StructuredControlFlowEmitter(
    IStructuredConditionValueEmitter conditions,
    IControlFlowDispatcherEmitter dispatchers,
    IArrayMetadataSnapshotEmitter arraySnapshots,
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
        Action<StructuredExceptionRegion, MethodEmissionContext, int?, int?, int?, int?>
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
        // Labeled branches stay inside labeled regions, conditionals and
        // loops, so every other construct starts without labels.
        Emit(
            code,
            method,
            sequence,
            context,
            emitBlock,
            emitExceptionRegion,
            loopBreakDepth,
            loopContinueDepth,
            exceptionLeaveDepth,
            dispatcherContinueDepth,
            ImmutableDictionary<StructuredBlockId, int>.Empty);
    }

    private void Emit(
        IWasmInstructionWriter code,
        StructuredMethod method,
        StructuredSequence sequence,
        MethodEmissionContext context,
        Func<StructuredBlockOccurrence, bool, bool, ConditionValue> emitBlock,
        Action<StructuredExceptionRegion, MethodEmissionContext, int?, int?, int?, int?>
            emitExceptionRegion,
        int? loopBreakDepth,
        int? loopContinueDepth,
        int? exceptionLeaveDepth,
        int? dispatcherContinueDepth,
        ImmutableDictionary<StructuredBlockId, int> labelDepths)
    {

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
                case StructuredLabeledBlock labeled:
                    EmitLabeled(code, WasmOpcodes.Block, labeled.Next, labeled.Body, method, context, emitBlock,
                        emitExceptionRegion, loopBreakDepth, loopContinueDepth, exceptionLeaveDepth,
                        dispatcherContinueDepth, labelDepths);
                    break;
                case StructuredLabeledLoop labeled:
                    EmitLabeled(code, WasmOpcodes.Loop, labeled.Header, labeled.Body, method, context, emitBlock,
                        emitExceptionRegion, loopBreakDepth, loopContinueDepth, exceptionLeaveDepth,
                        dispatcherContinueDepth, labelDepths);
                    break;
                case StructuredLabelBranch branch:
                    code.Write(WasmInstruction.WithOperand(
                        WasmOpcodes.Branch,
                        WasmInstructionOperand.Unsigned((uint)(labelDepths.TryGetValue(branch.Target, out var depth)
                            ? depth
                            : throw new InvalidOperationException(
                                "structured label branch was emitted outside its labeled region")))));
                    break;
                case StructuredExceptionRegion exception:
                    emitExceptionRegion(
                        exception,
                        context,
                        loopBreakDepth,
                        loopContinueDepth,
                        dispatcherContinueDepth,
                        exceptionLeaveDepth);
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
                        (body, dispatcherLoopDepth) => Emit(
                            code,
                            method,
                            body,
                            context,
                            emitBlock,
                            emitExceptionRegion,
                            loopBreakDepth: loopBreakDepth is int dispatcherBreakDepth
                                ? dispatcherBreakDepth + dispatcherLoopDepth + 2
                                : null,
                            loopContinueDepth:
                                loopContinueDepth is int dispatcherContinueLoopDepth
                                    ? dispatcherContinueLoopDepth + dispatcherLoopDepth + 2
                                    : null,
                            exceptionLeaveDepth:
                                exceptionLeaveDepth is int dispatcherLeaveDepth
                                    ? dispatcherLeaveDepth + dispatcherLoopDepth + 2
                                    : null,
                            dispatcherContinueDepth: dispatcherLoopDepth));
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
                        dispatcherContinueDepth,
                        labelDepths);
                    break;
                case StructuredLoop { Condition: { } rotatedCondition } loop
                    when Rotates(context.LoopRotation, method, rotatedCondition.Block):
                    arraySnapshots.Emit(code, context, rotatedCondition.Block);
                    EmitRotatedLoop(
                        code,
                        loop,
                        rotatedCondition,
                        method,
                        context,
                        emitBlock,
                        emitExceptionRegion,
                        loopBreakDepth,
                        loopContinueDepth,
                        exceptionLeaveDepth,
                        dispatcherContinueDepth,
                        labelDepths);
                    break;
                case StructuredLoop loop:
                    if (loop.Condition is { } loopCondition)
                    {
                        arraySnapshots.Emit(code, context, loopCondition.Block);
                    }
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
                        dispatcherContinueDepth,
                        labelDepths);
                    break;
                case StructuredPostTestLoop loop:
                    arraySnapshots.Emit(code, context, loop.Condition.Block);
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
                        dispatcherContinueDepth,
                        labelDepths);
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
        Action<StructuredExceptionRegion, MethodEmissionContext, int?, int?, int?, int?>
            emitExceptionRegion,
        int? loopBreakDepth,
        int? loopContinueDepth,
        int? exceptionLeaveDepth,
        int? dispatcherContinueDepth,
        ImmutableDictionary<StructuredBlockId, int> labelDepths)
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
                : null,
            Nested(labelDepths));
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
                    : null,
                Nested(labelDepths));
        }
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    // CallFree leaves a loop alone when its condition calls: the bottom copy
    // would be the callee's second call site.
    private static bool Rotates(LoopRotationPolicy policy, StructuredMethod method, StructuredBlockId condition) =>
        policy switch
        {
            LoopRotationPolicy.Guarded => true,
            LoopRotationPolicy.CallFree => !method.Blocks[condition].Instructions.Any(instruction =>
                instruction.Operation is CilOperation.Call or CilOperation.CallVirtual or
                    CilOperation.CallIndirect or CilOperation.NewObject),
            _ => false,
        };

    // A Wasm block or loop whose label is the named block's.
    private void EmitLabeled(
        IWasmInstructionWriter code,
        byte opcode,
        StructuredBlockId label,
        StructuredSequence body,
        StructuredMethod method,
        MethodEmissionContext context,
        Func<StructuredBlockOccurrence, bool, bool, ConditionValue> emitBlock,
        Action<StructuredExceptionRegion, MethodEmissionContext, int?, int?, int?, int?>
            emitExceptionRegion,
        int? loopBreakDepth,
        int? loopContinueDepth,
        int? exceptionLeaveDepth,
        int? dispatcherContinueDepth,
        ImmutableDictionary<StructuredBlockId, int> labelDepths)
    {
        code.Write(WasmInstruction.WithOperand(
            opcode,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        Emit(
            code,
            method,
            body,
            context,
            emitBlock,
            emitExceptionRegion,
            loopBreakDepth + 1,
            loopContinueDepth + 1,
            exceptionLeaveDepth + 1,
            dispatcherContinueDepth + 1,
            Nested(labelDepths).SetItem(label, 0));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private static ImmutableDictionary<StructuredBlockId, int> Nested(
        ImmutableDictionary<StructuredBlockId, int> labelDepths,
        int levels = 1) =>
        labelDepths.IsEmpty
            ? labelDepths
            : labelDepths.ToImmutableDictionary(label => label.Key, label => label.Value + levels);

    private void EmitLoop(
        IWasmInstructionWriter code,
        StructuredLoop loop,
        StructuredMethod method,
        MethodEmissionContext context,
        Func<StructuredBlockOccurrence, bool, bool, ConditionValue> emitBlock,
        Action<StructuredExceptionRegion, MethodEmissionContext, int?, int?, int?, int?>
            emitExceptionRegion,
        int? outerLoopBreakDepth,
        int? outerLoopContinueDepth,
        int? exceptionLeaveDepth,
        int? dispatcherContinueDepth,
        ImmutableDictionary<StructuredBlockId, int> labelDepths)
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
        // An unconditional loop runs its body until a break, return or
        // exception-region continuation leaves it.
        if (loop.Condition is { } condition)
        {
            EmitConditionValue(
                code,
                context,
                emitBlock(
                    condition,
                    true,
                    condition.Role == StructuredBlockRole.Owner));
            if (loop.ContinueWhenConditionTrue)
            {
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
            }
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.BranchIf,
                WasmInstructionOperand.Unsigned(1)));
        }
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
                : null,
            Nested(labelDepths, 4));
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
                : null,
            Nested(labelDepths, 3));
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
                : null,
            labelDepths: Nested(labelDepths, 1));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitRotatedLoop(
        IWasmInstructionWriter code,
        StructuredLoop loop,
        StructuredBlockOccurrence condition,
        StructuredMethod method,
        MethodEmissionContext context,
        Func<StructuredBlockOccurrence, bool, bool, ConditionValue> emitBlock,
        Action<StructuredExceptionRegion, MethodEmissionContext, int?, int?, int?, int?>
            emitExceptionRegion,
        int? outerLoopBreakDepth,
        int? outerLoopContinueDepth,
        int? exceptionLeaveDepth,
        int? dispatcherContinueDepth,
        ImmutableDictionary<StructuredBlockId, int> labelDepths)
    {
        // The condition guards entry and repeats at the bottom, so each
        // iteration falls through its body and takes only the back edge.
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Block,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Block,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        EmitConditionValue(
            code,
            context,
            emitBlock(
                condition,
                true,
                condition.Role == StructuredBlockRole.Owner));
        if (loop.ContinueWhenConditionTrue)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        }
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.BranchIf,
            WasmInstructionOperand.Unsigned(0)));
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
            exceptionLeaveDepth is int leaveDepth ? leaveDepth + 4 : null,
            dispatcherContinueDepth is int bodyDispatcherDepth
                ? bodyDispatcherDepth + 4
                : null,
            Nested(labelDepths, 4));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        // A continue in the continuation still reaches the condition.
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Block,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        Emit(
            code,
            method,
            loop.ContinueBody,
            context,
            emitBlock,
            emitExceptionRegion,
            3,
            0,
            exceptionLeaveDepth is int continueLeaveDepth
                ? continueLeaveDepth + 4
                : null,
            dispatcherContinueDepth is int continueDispatcherDepth
                ? continueDispatcherDepth + 4
                : null,
            Nested(labelDepths, 4));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        EmitConditionValue(code, context, emitBlock(condition, true, false));
        if (!loop.ContinueWhenConditionTrue)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        }
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.BranchIf,
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
                : null,
            labelDepths: Nested(labelDepths, 1));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitPostTestLoop(
        IWasmInstructionWriter code,
        StructuredPostTestLoop loop,
        StructuredMethod method,
        MethodEmissionContext context,
        Func<StructuredBlockOccurrence, bool, bool, ConditionValue> emitBlock,
        Action<StructuredExceptionRegion, MethodEmissionContext, int?, int?, int?, int?>
            emitExceptionRegion,
        int? outerLoopBreakDepth,
        int? outerLoopContinueDepth,
        int? exceptionLeaveDepth,
        int? dispatcherContinueDepth,
        ImmutableDictionary<StructuredBlockId, int> labelDepths)
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
            // Post-test structuring maps continue markers to the loop header,
            // not to the condition following this body block.
            1,
            exceptionLeaveDepth is int bodyLeaveDepth ? bodyLeaveDepth + 4 : null,
            dispatcherContinueDepth is int bodyDispatcherDepth
                ? bodyDispatcherDepth + 4
                : null,
            Nested(labelDepths, 4));
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
            loopBreakDepth: 3,
            loopContinueDepth: 1,
            exceptionLeaveDepth: exceptionLeaveDepth is int continueLeaveDepth
                ? continueLeaveDepth + 4
                : null,
            dispatcherContinueDepth: dispatcherContinueDepth is int continueDispatcherDepth
                ? continueDispatcherDepth + 4
                : null,
            labelDepths: Nested(labelDepths, 4));
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
                : null,
            labelDepths: Nested(labelDepths, 1));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitConditionValue(
        IWasmInstructionWriter code,
        MethodEmissionContext context,
        ConditionValue condition)
        => conditions.Emit(code, context, condition);
}
