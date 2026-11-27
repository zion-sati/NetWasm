// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal enum LandingPadRestoreStep
{
    // After the prologue: reads the arenas a method's blocks restore from marks taken there (EntryMark).
    MethodEntry,

    // As a protected block begins: reads the arenas its restore takes from marks taken there (BlockMark).
    BlockEntry,

    // At the block's landing pad: returns the arenas to where the method stood as the block began, from the method's
    // value frame (frames_unwind) or from marks (frame_marks_rewind).
    Pad,
}

internal interface ILandingPadRestoreEmitter
{
    // BlockEntry and Pad name the protected block; MethodEntry names none.
    void Emit(IWasmInstructionWriter code, LandingPadRestoreStep step, MethodEmissionContext context,
        StructuredExceptionGroupId? group = null);
}

// The reads and the pad's one call. A method that holds its own frame in an arena restores it from the frame's local, so
// neither its prologue nor its blocks read that arena; the reads are one load each, which Binaryen inlines. They stay two
// loads of exactly what a frame's entry or leave stores: one wider load of both cursors is not forwarded from such a
// store and stalls (measured: one blocked store forward per protected call, about 8 cycles each).
internal sealed class LandingPadRestoreEmitter(ITargetLayout layouts, IRuntimeImportResolver runtimeImports)
    : ILandingPadRestoreEmitter
{
    private readonly ITargetLayout _layouts = layouts ?? throw new ArgumentNullException(nameof(layouts));
    private readonly IRuntimeImportResolver _runtimeImports =
        runtimeImports ?? throw new ArgumentNullException(nameof(runtimeImports));

    public void Emit(IWasmInstructionWriter code, LandingPadRestoreStep step, MethodEmissionContext context,
        StructuredExceptionGroupId? group = null)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(context);
        switch (step)
        {
            case LandingPadRestoreStep.MethodEntry:
                if (context.EntryMarks is not { } method) return;
                var plans = context.LandingPadRestores.Values;
                if (plans.Any(plan => plan.Root == ArenaRestoreSource.EntryMark))
                    Read(code, RuntimeImportSymbol.FrameMarkRoot, method.Root, context);
                if (plans.Any(plan => plan.Value == ArenaRestoreSource.EntryMark))
                    Read(code, RuntimeImportSymbol.FrameMarkValueCursor, method.ValueCursor, context);
                if (plans.Any(plan => plan.StackTrace == ArenaRestoreSource.EntryMark))
                    Read(code, RuntimeImportSymbol.FrameMarkStackTrace, method.StackTrace, context);
                return;
            case LandingPadRestoreStep.BlockEntry:
            {
                var (plan, marks) = Block(context, group);
                if (plan.Root == ArenaRestoreSource.BlockMark) Read(code, RuntimeImportSymbol.FrameMarkRoot, marks.Root, context);
                if (plan.Value == ArenaRestoreSource.BlockMark)
                    Read(code, RuntimeImportSymbol.FrameMarkValueCursor, marks.ValueCursor, context);
                return;
            }
            case LandingPadRestoreStep.Pad:
            {
                var (plan, marks) = Block(context, group);
                var entry = context.EntryMarks ?? marks;
                switch (plan.Root)
                {
                    case ArenaRestoreSource.OwnedFrame:
                        Get(code, context.RootFrame);
                        ManagedMemoryEmitter.EmitAddressConstant(code, _layouts.Target,
                            checked(context.RootSlotCount * _layouts.Target.ObjectReferenceSize));
                        code.Write(WasmInstruction.NoOperand(
                            _layouts.Target.UsesMemory64 ? WasmOpcodes.I64Add : WasmOpcodes.I32Add));
                        break;
                    case ArenaRestoreSource.EntryMark:
                        Get(code, entry.Root);
                        break;
                    case ArenaRestoreSource.BlockMark:
                        Get(code, marks.Root);
                        break;
                    default:
                        throw new InvalidOperationException("A root arena is always restored.");
                }
                if (plan.Value == ArenaRestoreSource.OwnedFrame)
                {
                    // The method's value frame and its size; the runtime finds its chunk and map in its header.
                    Get(code, context.ValueFrame);
                    ManagedMemoryEmitter.EmitAddressConstant(code, _layouts.Target, context.ValueLayout.Size);
                    StackTrace(code, plan, entry);
                    Call(code, RuntimeImportSymbol.FramesUnwind, context);
                    return;
                }
                // The value arena's cursor: the runtime finds the value list's head and the cursor's chunk from it.
                Get(code, (plan.Value == ArenaRestoreSource.BlockMark ? marks : entry).ValueCursor);
                StackTrace(code, plan, entry);
                Call(code, RuntimeImportSymbol.FrameMarksRewind, context);
                return;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(step));
        }
    }

    private static (LandingPadRestore Plan, RegionMarkLocals Marks) Block(MethodEmissionContext context,
        StructuredExceptionGroupId? group)
    {
        var id = group ?? throw new ArgumentNullException(nameof(group), "BlockEntry and Pad name a protected block.");
        return (context.LandingPadRestores[id], context.RegionMarkLocals[id]);
    }

    private static void StackTrace(IWasmInstructionWriter code, LandingPadRestore plan, RegionMarkLocals entry)
    {
        if (plan.StackTrace == ArenaRestoreSource.EntryMark) Get(code, entry.StackTrace);
        else code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(-1)));
    }

    private void Read(IWasmInstructionWriter code, RuntimeImportSymbol symbol, int local, MethodEmissionContext context)
    {
        Call(code, symbol, context);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)local)));
    }

    private void Call(IWasmInstructionWriter code, RuntimeImportSymbol symbol, MethodEmissionContext context) =>
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)_runtimeImports.Resolve(symbol, context.RuntimeImportSelection))));

    private static void Get(IWasmInstructionWriter code, int local) =>
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)local)));
}
