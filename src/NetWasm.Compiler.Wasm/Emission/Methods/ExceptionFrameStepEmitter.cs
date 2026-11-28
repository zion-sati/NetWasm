// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

// A step of a protected block's frame for the first pass of exception dispatch (the runtime's exception_frame_*).
internal enum ExceptionFrameStep
{
    // Before the protected block: registers its frame, keeping the token in the group's frame local.
    Enter,

    // On the protected block's normal exit: pops the frame.
    Leave,

    // At the landing pad: rewinds the frame arenas to where the block began and pops the frame.
    Unwind,

    // At the landing pad, after Unwind: pushes the clause the first pass selected in this frame (from 1), or 0.
    TargetClause,

    // As a catch handler starts: the exception is no longer in flight.
    EndCatch,
}

internal interface IExceptionFrameStepEmitter
{
    void Emit(IWasmInstructionWriter code, ExceptionFrameStep step, StructuredMethod method,
        StructuredExceptionGroup group, MethodEmissionContext context,
        IEntryLookup<StructuredExceptionGroupKey, ExceptionGroupMetadata> exceptionMetadata);
}

// The block's strategy (IExceptionFrameStrategySelector) writes each step. A lazy root frame: the block keeps the frame
// local as it begins (entering the frame first when its handlers need it, so the frame predates the block's marks), and
// its landing pad restores the local after the rewind.
internal sealed class ExceptionFrameStepEmitter(
    IExceptionFrameStrategySelector strategies,
    ILazyRootFrameEmitter lazyRootFrames) : IExceptionFrameStepEmitter
{
    public void Emit(IWasmInstructionWriter code, ExceptionFrameStep step, StructuredMethod method,
        StructuredExceptionGroup group, MethodEmissionContext context,
        IEntryLookup<StructuredExceptionGroupKey, ExceptionGroupMetadata> exceptionMetadata)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(context);
        var lazy = context.LazyRootFrame;
        if (lazy is not null && step == ExceptionFrameStep.Enter)
        {
            if (lazy.EagerGroups.Contains(group.Id) && !lazy.Entered.Contains(group.TryOffset))
            {
                lazyRootFrames.Emit(code, LazyRootFrameStep.Ensure, context);
            }
            lazyRootFrames.Emit(code, LazyRootFrameStep.Save, context, group.Id);
        }
        strategies.Select(group, context.ReachesFilters).Emit(code, step, new ExceptionFrameRequest(method, group, context, exceptionMetadata));
    }
}
