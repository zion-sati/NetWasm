// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

// A protected block without catch clauses (finally, fault) registers no frame, since the first pass looks only for catch
// clauses: it does nothing as it ends, and its landing pad returns the arenas to where the method stood as it began
// (LandingPadRestore), reading a mark as it begins only where the method's own frames do not say.
internal sealed class MarkedExceptionRegion(ILandingPadRestoreEmitter restores, ILazyRootFrameEmitter lazyRootFrames)
    : IExceptionFrameStrategy
{
    public bool KeepsMarks => true;

    public void Emit(IWasmInstructionWriter code, ExceptionFrameStep step, ExceptionFrameRequest request)
    {
        var context = request.Context;
        switch (step)
        {
            case ExceptionFrameStep.Enter:
                restores.Emit(code, LandingPadRestoreStep.BlockEntry, context, request.Group.Id);
                return;
            case ExceptionFrameStep.Leave:
                return;
            case ExceptionFrameStep.Unwind:
                restores.Emit(code, LandingPadRestoreStep.Pad, context, request.Group.Id);
                if (context.LazyRootFrame is not null)
                {
                    lazyRootFrames.Emit(code, LazyRootFrameStep.Restore, context, request.Group.Id);
                }
                return;
            default:
                throw new InvalidOperationException("A protected block without catch clauses selects no clause and ends no catch.");
        }
    }
}

// A block with catch clauses registers its frame for the first pass, which selects the clause its landing pad takes.
internal sealed class RegisteredExceptionFrame(IExceptionFrameCallEmitter calls, ILazyRootFrameEmitter lazyRootFrames)
    : IExceptionFrameStrategy
{
    public bool KeepsMarks => false;

    public void Emit(IWasmInstructionWriter code, ExceptionFrameStep step, ExceptionFrameRequest request)
    {
        switch (step)
        {
            case ExceptionFrameStep.Enter:
                calls.Emit(code, ExceptionFrameCall.Enter, request);
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet,
                    WasmInstructionOperand.Unsigned((uint)request.Context.ExceptionFrameLocals[request.Group.Id])));
                calls.Emit(code, ExceptionFrameCall.SetEnvironment, request);
                break;
            case ExceptionFrameStep.Leave:
                calls.Emit(code, ExceptionFrameCall.Leave, request);
                break;
            case ExceptionFrameStep.Unwind:
                calls.Emit(code, ExceptionFrameCall.Unwind, request);
                if (request.Context.LazyRootFrame is not null)
                {
                    lazyRootFrames.Emit(code, LazyRootFrameStep.Restore, request.Context, request.Group.Id);
                }
                break;
            case ExceptionFrameStep.TargetClause:
                calls.Emit(code, ExceptionFrameCall.TargetClause, request);
                break;
            case ExceptionFrameStep.EndCatch:
                calls.Emit(code, ExceptionFrameCall.EndCatch, request);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(step));
        }
    }
}

// A pass-through catch (IPassThroughCatchLocalFinder) is passed by in the first pass while its local is nonzero: its
// token is -1, its landing pad restores the arenas (LandingPadRestore) and takes the handler (its only clause) without
// ending the catch, and the handler's rethrow continues the search the throw began. Not passed by, it registers only
// while a frame with filter clauses is registered, as a catch without filters does (FilterGuardedCatchFrame);
// otherwise its pad restores the arenas and selects its clause itself. In a program that reaches no filter
// (registersUnderFilters false) it never registers: its token is -1 passed by and 0 otherwise.
internal sealed class PassThroughExceptionFrame(
    ITargetLayout layouts,
    IExceptionFrameCallEmitter calls,
    IPassThroughCatchLocalFinder passThroughLocals,
    ILandingPadRestoreEmitter restores,
    ILazyRootFrameEmitter lazyRootFrames,
    bool registersUnderFilters = true) : IExceptionFrameStrategy
{
    public bool KeepsMarks => true;

    public void Emit(IWasmInstructionWriter code, ExceptionFrameStep step, ExceptionFrameRequest request)
    {
        var context = request.Context;
        var token = context.ExceptionFrameLocals[request.Group.Id];
        var passedBy = passThroughLocals.Find(request.Method, request.Group, context) ??
            throw new InvalidOperationException("A pass-through catch has its local.");
        var i32 = (byte)WasmValueTypes.FromCli(CliValueKind.I4, layouts.Target);
        // The token: -1 passed by (taken at entry), 0 caught without a frame, otherwise the registered frame's.
        void Registered()
        {
            Get(code, token);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(0)));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32GreaterThanSigned));
        }
        void Caught()
        {
            Get(code, token);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(-1)));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32NotEqual));
        }
        switch (step)
        {
            case ExceptionFrameStep.Enter:
                if (request.Group.Clauses.Any(clause => clause.FilterOffset is not null))
                {
                    throw new InvalidOperationException("A pass-through catch has no filter.");
                }
                restores.Emit(code, LandingPadRestoreStep.BlockEntry, context, request.Group.Id);
                Get(code, passedBy);
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(i32)));
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(-1)));
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
                if (!registersUnderFilters)
                {
                    code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(0)));
                    code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
                    code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)token)));
                    break;
                }
                calls.Emit(code, ExceptionFrameCall.FilterFramesLive, request);
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(i32)));
                calls.Emit(code, ExceptionFrameCall.Enter, request);
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(0)));
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)token)));
                break;
            case ExceptionFrameStep.Leave:
                Registered();
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
                calls.Emit(code, ExceptionFrameCall.Leave, request);
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
                break;
            case ExceptionFrameStep.Unwind:
                Registered();
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
                calls.Emit(code, ExceptionFrameCall.Unwind, request);
                if (context.LazyRootFrame is not null)
                {
                    lazyRootFrames.Emit(code, LazyRootFrameStep.Restore, context, request.Group.Id);
                }
                // Passed by or caught without a frame, the pad restores the arenas (LandingPadRestore): its handler runs the
                // method's own code (a passed-by handler restores contexts before it rethrows, and a Debug build marks
                // its lines), which needs the method's frames on top.
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
                restores.Emit(code, LandingPadRestoreStep.Pad, context, request.Group.Id);
                if (context.LazyRootFrame is not null)
                {
                    lazyRootFrames.Emit(code, LazyRootFrameStep.Restore, context, request.Group.Id);
                }
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
                break;
            case ExceptionFrameStep.TargetClause:
                Registered();
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(i32)));
                calls.Emit(code, ExceptionFrameCall.TargetClause, request);
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
                // Passed by, the frame's one clause handles the unwinding; otherwise the clause's own test selects it.
                Get(code, token);
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(i32)));
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(1)));
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
                calls.Emit(code, ExceptionFrameCall.SelectCatch, request);
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
                break;
            case ExceptionFrameStep.EndCatch:
                // Not passed by, the catch ends the exception's flight.
                Caught();
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
                calls.Emit(code, ExceptionFrameCall.EndCatch, request);
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(step));
        }
    }

    private static void Get(IWasmInstructionWriter code, int local) =>
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)local)));
}

// A catch without filters registers its frame only while a frame with filter clauses is registered
// (exception_filter_frames_live), since only then can the first pass run a filter it would have to stop at this catch
// before. With none registered, a catch the first pass cannot see changes nothing it does: as a finally does, its
// landing pad restores the arenas (LandingPadRestore), its token stays 0, and the pad selects its own clause
// (exception_catch_select), passing the exception on when no clause takes it, as a registered frame's pad does when the
// first pass selected nothing there. Registered, each step is the registered frame's. A block entered again (a loop, a
// runtime-async method resumed into it) asks again.
internal sealed class FilterGuardedCatchFrame(
    ITargetLayout layouts,
    IExceptionFrameCallEmitter calls,
    ILandingPadRestoreEmitter restores,
    ILazyRootFrameEmitter lazyRootFrames,
    IExceptionFrameStrategy registered) : IExceptionFrameStrategy
{
    public bool KeepsMarks => true;

    public void Emit(IWasmInstructionWriter code, ExceptionFrameStep step, ExceptionFrameRequest request)
    {
        var context = request.Context;
        var token = context.ExceptionFrameLocals[request.Group.Id];
        var i32 = (byte)WasmValueTypes.FromCli(CliValueKind.I4, layouts.Target);
        switch (step)
        {
            case ExceptionFrameStep.Enter:
                restores.Emit(code, LandingPadRestoreStep.BlockEntry, context, request.Group.Id);
                calls.Emit(code, ExceptionFrameCall.FilterFramesLive, request);
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(i32)));
                calls.Emit(code, ExceptionFrameCall.Enter, request);
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(0)));
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)token)));
                break;
            case ExceptionFrameStep.Leave:
                Get(code, token);
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
                registered.Emit(code, step, request);
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
                break;
            case ExceptionFrameStep.Unwind:
                Get(code, token);
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
                calls.Emit(code, ExceptionFrameCall.Unwind, request);
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
                restores.Emit(code, LandingPadRestoreStep.Pad, context, request.Group.Id);
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
                if (context.LazyRootFrame is not null)
                {
                    lazyRootFrames.Emit(code, LazyRootFrameStep.Restore, context, request.Group.Id);
                }
                break;
            case ExceptionFrameStep.TargetClause:
                Get(code, token);
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(i32)));
                calls.Emit(code, ExceptionFrameCall.TargetClause, request);
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
                calls.Emit(code, ExceptionFrameCall.SelectCatch, request);
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
                break;
            case ExceptionFrameStep.EndCatch:
                // Selected either way, the catch ends the exception's flight.
                calls.Emit(code, ExceptionFrameCall.EndCatch, request);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(step));
        }
    }

    private static void Get(IWasmInstructionWriter code, int local) =>
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)local)));
}

// A catch without filters in a program that reaches no filter: no first pass runs a filter, so the block never makes
// itself known to one. It does nothing as it ends, and its pad restores the arenas (LandingPadRestore) and selects its
// clause itself.
internal sealed class UnregisteredCatchFrame(
    IExceptionFrameCallEmitter calls,
    ILandingPadRestoreEmitter restores,
    ILazyRootFrameEmitter lazyRootFrames) : IExceptionFrameStrategy
{
    public bool KeepsMarks => true;

    public void Emit(IWasmInstructionWriter code, ExceptionFrameStep step, ExceptionFrameRequest request)
    {
        var context = request.Context;
        switch (step)
        {
            case ExceptionFrameStep.Enter:
                restores.Emit(code, LandingPadRestoreStep.BlockEntry, context, request.Group.Id);
                break;
            case ExceptionFrameStep.Leave:
                break;
            case ExceptionFrameStep.Unwind:
                restores.Emit(code, LandingPadRestoreStep.Pad, context, request.Group.Id);
                if (context.LazyRootFrame is not null)
                {
                    lazyRootFrames.Emit(code, LazyRootFrameStep.Restore, context, request.Group.Id);
                }
                break;
            case ExceptionFrameStep.TargetClause:
                calls.Emit(code, ExceptionFrameCall.SelectCatch, request);
                break;
            case ExceptionFrameStep.EndCatch:
                calls.Emit(code, ExceptionFrameCall.EndCatch, request);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(step));
        }
    }
}
