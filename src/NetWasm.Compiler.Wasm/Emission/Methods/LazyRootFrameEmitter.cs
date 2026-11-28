// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal enum LazyRootFrameStep
{
    // Before a safepoint that publishes a root, or a protected block that needs the frame from its start: enters the
    // frame, unless the frame local already holds one, and zeroes every slot that neither an authoritative variable's
    // copy nor the publication that follows writes.
    Ensure,

    // At a normal exit: leaves the frame if the method entered one.
    Leave,

    // Before a protected block: keeps the frame local as the block begins.
    Save,

    // At the block's landing pad, after the arenas were rewound to the block's marks: what the rewind popped was
    // entered after the block began, so the frame local returns to what it was then.
    Restore,

    // Pushes whether the method holds a frame (an i32), so the stores that clear slots run only then.
    Test,
}

internal interface ILazyRootFrameEmitter
{
    // Save and Restore name the protected block. Ensure may name the slots the publication right after it stores.
    void Emit(IWasmInstructionWriter code, LazyRootFrameStep step, MethodEmissionContext context,
        StructuredExceptionGroupId? group = null, IReadOnlySet<int>? publishedSlots = null);
}

internal sealed class LazyRootFrameEmitter(ITargetLayout layouts, IRuntimeImportResolver runtimeImports) : ILazyRootFrameEmitter
{
    private readonly ITargetLayout _layouts = layouts ?? throw new ArgumentNullException(nameof(layouts));
    private readonly IRuntimeImportResolver _runtimeImports = runtimeImports ?? throw new ArgumentNullException(nameof(runtimeImports));

    public void Emit(IWasmInstructionWriter code, LazyRootFrameStep step, MethodEmissionContext context,
        StructuredExceptionGroupId? group = null, IReadOnlySet<int>? publishedSlots = null)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(context);
        switch (step)
        {
            case LazyRootFrameStep.Ensure:
                Get(code, context.RootFrame);
                Absent(code);
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(context.RootSlotCount)));
                Call(code, RuntimeImportSymbol.RootFrameEnter);
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)context.RootFrame)));
                var written = context.AuthoritativeRootSlots.Values.ToHashSet();
                if (publishedSlots is not null) written.UnionWith(publishedSlots);
                FrameSlotZeroing.Emit(code, _layouts.Target, writer => Get(writer, context.RootFrame), context.RootSlotCount,
                    written);
                // Each authoritative slot takes its variable's value, which stores made before the frame did not write.
                foreach (var (source, slot) in context.AuthoritativeRootSlots.OrderBy(pair => pair.Value))
                {
                    Get(code, context.RootFrame);
                    Get(code, source.Kind == RootSourceKind.Local ? context.LocalBase + source.Index : context.ArgumentLocal(source.Index));
                    ManagedMemoryEmitter.EmitStoreBySize(code, _layouts.Target,
                        checked(slot * _layouts.Target.ObjectReferenceSize), _layouts.Target.ObjectReferenceSize);
                }
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
                break;
            case LazyRootFrameStep.Leave:
                Emit(code, LazyRootFrameStep.Test, context);
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
                Get(code, context.RootFrame);
                Call(code, RuntimeImportSymbol.RootFrameLeave);
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
                break;
            case LazyRootFrameStep.Save:
                Get(code, context.RootFrame);
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet,
                    WasmInstructionOperand.Unsigned((uint)context.RootFrameSaves[Group(group)])));
                break;
            case LazyRootFrameStep.Restore:
                Get(code, context.RootFrameSaves[Group(group)]);
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)context.RootFrame)));
                break;
            case LazyRootFrameStep.Test:
                Get(code, context.RootFrame);
                if (_layouts.Target.UsesMemory64)
                {
                    code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64EqualZero));
                    code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(step));
        }
    }

    private static StructuredExceptionGroupId Group(StructuredExceptionGroupId? group) =>
        group ?? throw new ArgumentNullException(nameof(group), "Save and Restore name a protected block.");

    // Pushes whether the frame local is 0.
    private void Absent(IWasmInstructionWriter code) => code.Write(WasmInstruction.NoOperand(
        _layouts.Target.UsesMemory64 ? WasmOpcodes.I64EqualZero : WasmOpcodes.I32EqualZero));

    private static void Get(IWasmInstructionWriter code, int local) =>
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)local)));

    private void Call(IWasmInstructionWriter code, RuntimeImportSymbol symbol) =>
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)_runtimeImports.Resolve(symbol))));
}
