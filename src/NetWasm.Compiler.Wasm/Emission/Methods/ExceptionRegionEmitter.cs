// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed class ExceptionRegionEmitter(
    ITargetLayout layouts,
    IExceptionPayloadBlockEmitter exceptions,
    IExceptionFrameStepEmitter frames,
    IPassThroughCatchLocalFinder passThroughLocals,
    IRuntimeImportResolver runtimeImports) : IExceptionRegionEmitter
{
    public void Emit(
        IWasmInstructionWriter code,
        StructuredMethod method,
        StructuredExceptionRegion region,
        MethodEmissionContext context,
        IEntryLookup<StructuredExceptionGroupKey, ExceptionGroupMetadata> exceptionMetadata,
        Action<StructuredSequence, MethodEmissionContext, int?, int?, int?> emitSequence,
        int? loopBreakDepth = null,
        int? loopContinueDepth = null,
        int? dispatcherContinueDepth = null,
        int? enclosingLeaveDepth = null)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(exceptionMetadata);
        ArgumentNullException.ThrowIfNull(emitSequence);

        if (!method.ExceptionGroups.TryGetValue(region.Group, out var group))
        {
            throw new InvalidOperationException(
                $"structured exception group '{region.Group.Value}' is not defined");
        }

        var ownsDefinition = context.EmittedExceptionGroups.Add(group.Id);
        context = context with
        {
            RecordOriginalBlockOwnership = context.RecordOriginalBlockOwnership && ownsDefinition,
        };

        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(0)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)context.ExceptionContinuationLocals[group.Id])));
        if (group.Clauses.All(clause => clause.Kind is
                CilExceptionRegionKind.Catch or CilExceptionRegionKind.Filter))
        {
            EmitCatchGroup(
                code,
                method,
                group,
                context,
                exceptionMetadata,
                emitSequence,
                region,
                loopBreakDepth,
                loopContinueDepth,
                dispatcherContinueDepth,
                enclosingLeaveDepth);
            return;
        }
        if (group.Clauses.Length == 1)
        {
            EmitCleanupGroup(
                code,
                method,
                group,
                context,
                exceptionMetadata,
                emitSequence,
                region,
                loopBreakDepth,
                loopContinueDepth,
                dispatcherContinueDepth,
                enclosingLeaveDepth);
            return;
        }
        throw UnsupportedExceptionShape(
            method,
            "an exception group must contain typed catches or one finally clause");
    }

    private void EmitProtectedParts(
        IWasmInstructionWriter code,
        StructuredMethod method,
        StructuredExceptionGroup group,
        MethodEmissionContext context,
        IEntryLookup<StructuredExceptionGroupKey, ExceptionGroupMetadata> exceptionMetadata,
        Action<StructuredSequence, MethodEmissionContext, int?, int?, int?> emitSequence)
    {
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Block,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        foreach (var part in group.ProtectedParts)
        {
            switch (part)
            {
                case StructuredExceptionCode body:
                    emitSequence(
                        body.Body,
                        context with { ActiveExceptionGroup = group },
                        null,
                        null,
                        0);
                    break;
                case StructuredNestedExceptionGroup nested:
                    Emit(
                        code,
                        method,
                        new StructuredExceptionRegion(nested.Group, null),
                        context with { ActiveExceptionGroup = group },
                        exceptionMetadata,
                        emitSequence,
                        enclosingLeaveDepth: 0);
                    code.Write(WasmInstruction.WithOperand(
                        WasmOpcodes.LocalGet,
                        WasmInstructionOperand.Unsigned((uint)context.ExceptionContinuationLocals[group.Id])));
                    code.Write(WasmInstruction.WithOperand(
                        WasmOpcodes.BranchIf,
                        WasmInstructionOperand.Unsigned(0)));
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unknown exception part {part.GetType().Name}.");
            }
        }
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitCatchGroup(
        IWasmInstructionWriter code,
        StructuredMethod method,
        StructuredExceptionGroup group,
        MethodEmissionContext context,
        IEntryLookup<StructuredExceptionGroupKey, ExceptionGroupMetadata> exceptionMetadata,
        Action<StructuredSequence, MethodEmissionContext, int?, int?, int?> emitSequence,
        StructuredExceptionRegion region,
        int? loopBreakDepth,
        int? loopContinueDepth,
        int? dispatcherContinueDepth,
        int? enclosingLeaveDepth)
    {
        frames.Emit(code, ExceptionFrameStep.Enter, method, group, context, exceptionMetadata);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Block,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.TryTable,
            WasmInstructionOperand.TryTableCatch(
                WasmOpcodes.EmptyBlockType,
                0,
                0)));
        EmitProtectedParts(code, method, group, context, exceptionMetadata, emitSequence);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        frames.Emit(code, ExceptionFrameStep.Leave, method, group, context, exceptionMetadata);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Branch,
            WasmInstructionOperand.Unsigned(1)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)context.ExceptionTemporary)));
        frames.Emit(code, ExceptionFrameStep.Unwind, method, group, context, exceptionMetadata);
        EnterExceptionCell(code, group, context);
        frames.Emit(code, ExceptionFrameStep.TargetClause, method, group, context, exceptionMetadata);
        var clauseSelector = WasmLocalLayoutPlanner.GetEvaluationStackLocal(
            context.StackLocals,
            0,
            CliValueKind.I4,
            layouts.Target);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)clauseSelector)));
        for (var index = 0; index < group.Clauses.Length; index++)
        {
            var clause = group.Clauses[index];
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)clauseSelector)));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Constant,
                WasmInstructionOperand.Signed(index + 1)));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.If,
                WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            LoadExceptionRoot(code, group, context);
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalSet,
                WasmInstructionOperand.Unsigned((uint)WasmLocalLayoutPlanner.GetEvaluationStackLocal(
                    context.StackLocals,
                    0,
                    CliValueKind.ManagedReference,
                    layouts.Target))));
            frames.Emit(code, ExceptionFrameStep.EndCatch, method, group, context, exceptionMetadata);
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Block,
                WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            emitSequence(
                clause.HandlerBody,
                context with
                {
                    ActiveExceptionGroup = group,
                    ActiveCatchCellFrame = context.ExceptionCellFrames[group.Id],
                    ActiveCatchPassThroughToken = passThroughLocals.Find(method, group, context) is null
                        ? null
                        : context.ExceptionFrameLocals[group.Id],
                },
                null,
                null,
                0);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
            LeaveExceptionCell(code, group, context);
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Branch,
                WasmInstructionOperand.Unsigned(1)));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
        LoadExceptionRoot(code, group, context);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Throw,
            WasmInstructionOperand.Unsigned(0)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        EmitNormalContinuations(
            code,
            group,
            context,
            emitSequence,
            region,
            loopBreakDepth,
            loopContinueDepth,
            dispatcherContinueDepth,
            enclosingLeaveDepth);
    }

    private void EmitCleanupGroup(
        IWasmInstructionWriter code,
        StructuredMethod method,
        StructuredExceptionGroup group,
        MethodEmissionContext context,
        IEntryLookup<StructuredExceptionGroupKey, ExceptionGroupMetadata> exceptionMetadata,
        Action<StructuredSequence, MethodEmissionContext, int?, int?, int?> emitSequence,
        StructuredExceptionRegion region,
        int? loopBreakDepth,
        int? loopContinueDepth,
        int? dispatcherContinueDepth,
        int? enclosingLeaveDepth)
    {
        var clause = group.Clauses[0];
        if (clause.Kind == CilExceptionRegionKind.Finally)
        {
            EmitFinallyGroup(
                code,
                method,
                group,
                context,
                exceptionMetadata,
                emitSequence,
                region,
                loopBreakDepth,
                loopContinueDepth,
                dispatcherContinueDepth,
                enclosingLeaveDepth);
            return;
        }
        frames.Emit(code, ExceptionFrameStep.Enter, method, group, context, exceptionMetadata);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Block,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.TryTable,
            WasmInstructionOperand.TryTableCatch(
                WasmOpcodes.EmptyBlockType,
                0,
                0)));
        EmitProtectedParts(code, method, group, context, exceptionMetadata, emitSequence);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        frames.Emit(code, ExceptionFrameStep.Leave, method, group, context, exceptionMetadata);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Branch,
            WasmInstructionOperand.Unsigned(1)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)context.ExceptionTemporary)));
        frames.Emit(code, ExceptionFrameStep.Unwind, method, group, context, exceptionMetadata);
        EnterExceptionCell(code, group, context);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Block,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        emitSequence(
            clause.HandlerBody,
            context with { ActiveExceptionGroup = group },
            null,
            null,
            0);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        LoadExceptionRoot(code, group, context);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Throw,
            WasmInstructionOperand.Unsigned(0)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        EmitNormalContinuations(
            code,
            group,
            context,
            emitSequence,
            region,
            loopBreakDepth,
            loopContinueDepth,
            dispatcherContinueDepth,
            enclosingLeaveDepth);
    }

    private void EmitFinallyGroup(
        IWasmInstructionWriter code,
        StructuredMethod method,
        StructuredExceptionGroup group,
        MethodEmissionContext context,
        IEntryLookup<StructuredExceptionGroupKey, ExceptionGroupMetadata> exceptionMetadata,
        Action<StructuredSequence, MethodEmissionContext, int?, int?, int?> emitSequence,
        StructuredExceptionRegion region,
        int? loopBreakDepth,
        int? loopContinueDepth,
        int? dispatcherContinueDepth,
        int? enclosingLeaveDepth)
    {
        var clause = group.Clauses[0];
        // The normal path has no exception cell; the landing pad enters one.
        EmitAddressConstant(code, 0);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)context.ExceptionCellFrames[group.Id])));
        frames.Emit(code, ExceptionFrameStep.Enter, method, group, context, exceptionMetadata);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Block,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.TryTable,
            WasmInstructionOperand.TryTableCatch(
                WasmOpcodes.EmptyBlockType,
                0,
                0)));
        EmitProtectedParts(code, method, group, context, exceptionMetadata, emitSequence);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        frames.Emit(code, ExceptionFrameStep.Leave, method, group, context, exceptionMetadata);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Branch,
            WasmInstructionOperand.Unsigned(1)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)context.ExceptionTemporary)));
        frames.Emit(code, ExceptionFrameStep.Unwind, method, group, context, exceptionMetadata);
        EnterExceptionCell(code, group, context);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));

        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Block,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        // The protected body's leave already selected a normal continuation.
        // Run the entire finally before dispatching that saved destination;
        // testing its selector between handler regions would truncate cleanup.
        emitSequence(
            clause.HandlerBody,
            context with { ActiveExceptionGroup = group },
            null,
            null,
            null);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));

        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)context.ExceptionCellFrames[group.Id])));
        if (layouts.Target.UsesMemory64)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64EqualZero));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        }
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        LoadExceptionRoot(code, group, context);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Throw,
            WasmInstructionOperand.Unsigned(0)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));

        EmitNormalContinuations(
            code,
            group,
            context,
            emitSequence,
            region,
            loopBreakDepth,
            loopContinueDepth,
            dispatcherContinueDepth,
            enclosingLeaveDepth);
    }

    private static void EmitNormalContinuations(
        IWasmInstructionWriter code,
        StructuredExceptionGroup group,
        MethodEmissionContext context,
        Action<StructuredSequence, MethodEmissionContext, int?, int?, int?> emitSequence,
        StructuredExceptionRegion region,
        int? loopBreakDepth,
        int? loopContinueDepth,
        int? dispatcherContinueDepth,
        int? enclosingLeaveDepth)
    {
        var selector = context.ExceptionContinuationLocals[group.Id];
        if (group.ContinuationDispatcher is { } dispatcher)
        {
            var defaultIndex = region.FallthroughContinuation?.Value ?? 0;
            SetDispatcherTarget(group.NormalContinuations[defaultIndex]);
            for (var index = 0; index < group.NormalContinuations.Length; index++)
            {
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.LocalGet,
                    WasmInstructionOperand.Unsigned((uint)selector)));
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.I32Constant,
                    WasmInstructionOperand.Signed(index + 1)));
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.If,
                    WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
                SetDispatcherTarget(group.NormalContinuations[index]);
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
            }
            return;

            void SetDispatcherTarget(StructuredExceptionContinuation continuation)
            {
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.I32Constant,
                    WasmInstructionOperand.Signed(continuation.Target.Value)));
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.LocalSet,
                    WasmInstructionOperand.Unsigned((uint)context.DispatcherProgramCounter)));
            }
        }
        for (var index = 0; index < group.NormalContinuations.Length; index++)
        {
            if (index == region.FallthroughContinuation?.Value)
            {
                continue;
            }
            var continuationId = new StructuredContinuationId(index);
            EmitContinuationSelected(
                code,
                selector,
                index,
                region.DispatcherContinuations.Contains(continuationId) &&
                index == 0);
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.If,
                WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            var continuation = group.NormalContinuations[index];
            if (region.DispatcherContinuations.Contains(continuationId))
            {
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.I32Constant,
                    WasmInstructionOperand.Signed(continuation.Target.Value)));
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.LocalSet,
                    WasmInstructionOperand.Unsigned(
                        (uint)context.DispatcherProgramCounter)));
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.Branch,
                    WasmInstructionOperand.Unsigned((uint)(
                        (dispatcherContinueDepth ?? throw new InvalidOperationException(
                            "an exception continuation routed through a dispatcher " +
                            "was emitted outside that dispatcher")) + 1))));
            }
            else if (context.ActiveExceptionGroup is { } parent &&
                TryGetContinuationIndex(parent, continuation.Target, out var parentIndex))
            {
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.I32Constant,
                    WasmInstructionOperand.Signed(parentIndex + 1)));
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.LocalSet,
                    WasmInstructionOperand.Unsigned((uint)context.ExceptionContinuationLocals[parent.Id])));
            }
            else
            {
                // A leave to an enclosing group's continuation ends this body as it
                // would end the enclosing protected code.
                emitSequence(
                    continuation.Body,
                    context,
                    loopBreakDepth is int breakDepth ? breakDepth + 1 : null,
                    loopContinueDepth is int continueDepth ? continueDepth + 1 : null,
                    enclosingLeaveDepth is int leaveDepth ? leaveDepth + 1 : null);
            }
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
    }

    private static void EmitContinuationSelected(
        IWasmInstructionWriter code,
        int selector,
        int continuationIndex,
        bool includeDefault)
    {
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)selector)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(continuationIndex + 1)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        if (!includeDefault)
        {
            return;
        }
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)selector)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Or));
    }

    private static bool TryGetContinuationIndex(
        StructuredExceptionGroup group,
        StructuredBlockId target,
        out int index)
    {
        for (index = 0; index < group.NormalContinuations.Length; index++)
        {
            if (group.NormalContinuations[index].Target == target)
            {
                return true;
            }
        }
        index = -1;
        return false;
    }

    private void EmitAddressConstant(IWasmInstructionWriter code, int value)
    {
        if (layouts.Target.UsesMemory64)
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I64Constant,
                WasmInstructionOperand.Signed64(value)));
        }
        else
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Constant,
                WasmInstructionOperand.Signed(value)));
        }
    }

    // At a landing pad, after the frame arenas were rewound: a one-slot root frame of the block's own holds the exception
    // in flight while its handler runs. Nothing between the catch and here collects. The runtime enters it out of line,
    // so the pad stays one call where an inlined frame entry would grow the method around its normal path.
    private void EnterExceptionCell(
        IWasmInstructionWriter code,
        StructuredExceptionGroup group,
        MethodEmissionContext context)
    {
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)context.ExceptionTemporary)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
            RuntimeImportSymbol.ExceptionCellEnter, context.RuntimeImportSelection))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)context.ExceptionCellFrames[group.Id])));
    }

    private void LoadExceptionRoot(
        IWasmInstructionWriter code,
        StructuredExceptionGroup group,
        MethodEmissionContext context)
    {
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)context.ExceptionCellFrames[group.Id])));
        ManagedMemoryEmitter.EmitLoadBySize(code, layouts.Target, 0, layouts.Target.ObjectReferenceSize);
    }

    // A catch handler that ends normally leaves its exception cell, the newest root frame: the handler's own calls
    // left theirs.
    private void LeaveExceptionCell(
        IWasmInstructionWriter code,
        StructuredExceptionGroup group,
        MethodEmissionContext context)
    {
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)context.ExceptionCellFrames[group.Id])));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
            RuntimeImportSymbol.ExceptionCellLeave, context.RuntimeImportSelection))));
    }

    private static CompilerException UnsupportedExceptionShape(
        StructuredMethod method,
        string message) => new(new CompilerDiagnostic(
            DiagnosticCode.UnsupportedCil,
            message,
            method.Header.Method.Name));
}
