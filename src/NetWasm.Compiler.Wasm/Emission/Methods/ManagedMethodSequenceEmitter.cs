using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed class ManagedMethodSequenceEmitter(
    ITargetLayout layouts,
    IExceptionRegionEmitter exceptionRegions,
    IStructuredControlFlowEmitter controlFlow,
    IStructuredLeaveEmitter leaves,
    IBranchComparisonEmitter branchComparisons,
    ICilInstructionDispatcher instructionDispatcher) : IManagedMethodSequenceEmitter
{
    public ManagedMethodSequenceEmission Emit(
        IWasmInstructionWriter code,
        StructuredMethod method,
        StructuredSequence sequence,
        MethodEmissionContext context,
        InstructionModuleTarget target,
        IFunctionIndexResolver functionIndices,
        int? loopBreakDepth = null,
        int? loopContinueDepth = null,
        int? exceptionLeaveDepth = null)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(sequence);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(functionIndices);

        var blockEmissionCounts = new Dictionary<int, int>();
        var explicitlyOriginalBlocks = new HashSet<int>();
        EmitSequence(
            code,
            method,
            method.Header,
            sequence,
            context,
            target,
            functionIndices,
            blockEmissionCounts,
            explicitlyOriginalBlocks,
            loopBreakDepth,
            loopContinueDepth,
            exceptionLeaveDepth);
        return new(blockEmissionCounts.ToImmutableDictionary());
    }

    private void EmitSequence(
        IWasmInstructionWriter code,
        StructuredMethod method,
        StructuredMethodHeader header,
        StructuredSequence sequence,
        MethodEmissionContext context,
        InstructionModuleTarget target,
        IFunctionIndexResolver functionIndices,
        Dictionary<int, int> blockEmissionCounts,
        HashSet<int> explicitlyOriginalBlocks,
        int? loopBreakDepth,
        int? loopContinueDepth,
        int? exceptionLeaveDepth)
    {
        controlFlow.Emit(
            code,
            method,
            sequence,
            context,
            (block, includeConditionalTerminator, recordOriginal) => EmitBlock(
                code,
                method,
                header,
                block,
                includeConditionalTerminator,
                recordOriginal,
                context,
                target,
                functionIndices,
                blockEmissionCounts,
                explicitlyOriginalBlocks),
            (exception, exceptionContext, isOutermost,
                exceptionLoopBreakDepth, exceptionLoopContinueDepth,
                exceptionDispatcherContinueDepth) =>
                EmitExceptionGroup(
                    code,
                    method,
                    header,
                    exception,
                    exceptionContext,
                    target,
                    functionIndices,
                    blockEmissionCounts,
                    explicitlyOriginalBlocks,
                    isOutermost,
                    exceptionLoopBreakDepth,
                    exceptionLoopContinueDepth,
                    exceptionDispatcherContinueDepth),
            loopBreakDepth,
            loopContinueDepth,
            exceptionLeaveDepth: exceptionLeaveDepth);
    }

    private void EmitExceptionGroup(
        IWasmInstructionWriter code,
        StructuredMethod method,
        StructuredMethodHeader header,
        StructuredExceptionRegion exception,
        MethodEmissionContext context,
        InstructionModuleTarget target,
        IFunctionIndexResolver functionIndices,
        Dictionary<int, int> blockEmissionCounts,
        HashSet<int> explicitlyOriginalBlocks,
        bool isOutermost,
        int? loopBreakDepth,
        int? loopContinueDepth,
        int? dispatcherContinueDepth)
    {
        exceptionRegions.Emit(
            code,
            method,
            exception,
            context,
            isOutermost,
            target.ModuleData,
            (sequence, sequenceContext, sequenceLoopBreakDepth,
                sequenceLoopContinueDepth, exceptionLeaveDepth) => EmitSequence(
                code,
                method,
                header,
                sequence,
                sequenceContext,
                target,
                functionIndices,
                blockEmissionCounts,
                explicitlyOriginalBlocks,
                sequenceLoopBreakDepth,
                sequenceLoopContinueDepth,
                exceptionLeaveDepth),
            loopBreakDepth,
            loopContinueDepth,
            dispatcherContinueDepth);
    }

    private ConditionValue EmitBlock(
        IWasmInstructionWriter code,
        StructuredMethod structured,
        StructuredMethodHeader header,
        StructuredBlockOccurrence occurrence,
        bool includeConditionalTerminator,
        bool recordOriginal,
        MethodEmissionContext context,
        InstructionModuleTarget target,
        IFunctionIndexResolver functionIndices,
        Dictionary<int, int> blockEmissionCounts,
        HashSet<int> explicitlyOriginalBlocks)
    {
        var definition = structured.Blocks[occurrence.Block];
        var blockIndex = occurrence.Block.Value;
        if (recordOriginal)
        {
            if (explicitlyOriginalBlocks.Add(blockIndex))
            {
                blockEmissionCounts[blockIndex] = 1;
            }
            else
            {
                blockEmissionCounts[blockIndex]++;
            }
        }
        else
        {
            blockEmissionCounts.TryAdd(blockIndex, 1);
        }
        if (!recordOriginal &&
            context.ActiveExceptionGroup is null &&
            definition.Exit is StructuredLeaveExit)
        {
            // A dispatcher can retain an already-owned protected leave block as
            // a routing node after its exception cleanup has run. Re-emitting
            // that copy would repeat its body outside the owning exception group.
            return new ConditionValue(-1, CliValueKind.I4);
        }
        var stack = new List<CliValueKind>(
            definition.EntryStack);
        foreach (var instruction in definition.Instructions)
        {
            EmitInstruction(
                code,
                header,
                instruction,
                stack,
                context,
                target,
                functionIndices);
        }

        switch (definition.Exit)
        {
            case StructuredFallthroughExit:
                break;

            case StructuredBranchExit:
                // Branch targets are represented by the surrounding structured
                // sequence and must not be emitted as raw CIL branches.
                break;
            case StructuredLeaveExit leave:
                leaves.Emit(
                    code,
                    GetExitInstruction(structured, leave) ??
                        throw MissingExitInstruction(definition),
                    occurrence.LeaveContinuation,
                    context);
                break;
            case StructuredConditionalExit conditional:
                if (!includeConditionalTerminator)
                {
                    throw new InvalidOperationException(
                        "A conditional terminator was emitted outside a structured condition.");
                }
                var slot = conditional.Condition.StackSlot;
                var conditionType = conditional.Condition.LeftKind;
                if (conditional.Condition.RightKind is { } rightKind)
                {
                    code.Write(WasmInstruction.WithOperand(
                        WasmOpcodes.LocalGet,
                        WasmInstructionOperand.Unsigned((uint)GetStackLocal(
                            context.StackLocals, slot, conditional.Condition.LeftKind))));
                    code.Write(WasmInstruction.WithOperand(
                        WasmOpcodes.LocalGet,
                        WasmInstructionOperand.Unsigned((uint)GetStackLocal(
                            context.StackLocals, slot + 1, rightKind))));
                    branchComparisons.Compare(
                        code,
                        conditional.Condition.Operation,
                        conditional.Condition.LeftKind,
                        rightKind);
                    code.Write(WasmInstruction.WithOperand(
                        WasmOpcodes.LocalSet,
                        WasmInstructionOperand.Unsigned((uint)GetStackLocal(
                            context.StackLocals, slot, CliValueKind.I4))));
                    stack.RemoveAt(slot + 1);
                    conditionType = CliValueKind.I4;
                }
                stack.RemoveAt(slot);
                return new ConditionValue(slot, conditionType);
            case StructuredTerminalExit terminal:
                EmitInstruction(
                    code,
                    header,
                    terminal.Instruction,
                    stack,
                    context,
                    target,
                    functionIndices);
                break;
        }
        return new ConditionValue(-1, CliValueKind.I4);
    }

    private static CilInstruction? GetExitInstruction(
        StructuredMethod method,
        StructuredLeaveExit exit) =>
        FindInstruction(method, exit.InstructionOffset);

    private static CilInstruction? FindInstruction(
        StructuredMethod method,
        int offset)
    {
        foreach (var instruction in method.Header.Instructions)
        {
            if (instruction.Offset == offset)
            {
                return instruction;
            }
        }
        return null;
    }

    private static InvalidOperationException MissingExitInstruction(
        StructuredBlockDefinition definition) => new(
        $"Structured block {definition.Id.Value} has an exit without its instruction fact.");

    private void EmitInstruction(
        IWasmInstructionWriter code,
        StructuredMethodHeader header,
        CilInstruction instruction,
        List<CliValueKind> stack,
        MethodEmissionContext context,
        InstructionModuleTarget target,
        IFunctionIndexResolver functionIndices) =>
        instructionDispatcher.Emit(new(
            header,
                instruction,
                stack,
                context,
                target),
            code,
            functionIndices);

    private int GetStackLocal(
        EvaluationStackLocalLayout stackLocals,
        int slot,
        CliValueKind type) => WasmLocalLayoutPlanner.GetEvaluationStackLocal(
            stackLocals,
            slot,
            type,
            layouts.Target);
}
