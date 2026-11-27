// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.ControlFlow.Structured;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.AddressTakenValues;
using NetWasm.Compiler.Wasm.Emission.Values;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed class MethodFrameEntryEmitter(
    ITargetLayout layouts,
    IValueLayoutProvider values,
    IRuntimeImportResolver runtimeImports,
    IStackTraceFrameEntryEmitter stackTraceFrames,
    IValueFrameAddressEmitter valueFrameAddresses,
    IValueFrameEntryEmitter valueFrames,
    IFilterEnvironmentRootEmitter filterEnvironmentRoots,
    IManagedByRefAggregateInitializationEmitter managedByRefAggregates,
    ILandingPadRestoreEmitter landingPadRestores) :
    IMethodFrameEntryEmitter
{
    public void Emit(
        IWasmInstructionWriter code,
        StructuredMethodHeader header,
        MethodEmissionContext context)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(context);

        if (context.StackTraceMethodId != 0)
        {
            stackTraceFrames.Enter(
                code,
                context.StackTraceMethodId,
                context.RuntimeImportSelection);
        }

        // A lazy frame is entered where the method first publishes a root; its local starts 0.
        if (context.RootSlotCount != 0 && context.LazyRootFrame is null)
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Constant,
                WasmInstructionOperand.Signed(context.RootSlotCount)));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                    RuntimeImportSymbol.RootFrameEnter))));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalSet,
                WasmInstructionOperand.Unsigned((uint)context.RootFrame)));
            if (context.RootFrameStartsZeroed)
            {
                // Every slot, authoritative locals' among them, starts null; an authoritative argument's slot takes the
                // argument right after.
                FrameSlotZeroing.Emit(code, layouts.Target,
                    writer => writer.Write(WasmInstruction.WithOperand(
                        WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)context.RootFrame))),
                    context.RootSlotCount, RootFrameEntryClearPlanner.ArgumentSlots(context));
            }
            foreach (var (source, slot) in context.AuthoritativeRootSlots.OrderBy(pair => pair.Value))
            {
                if (context.RootFrameStartsZeroed && source.Kind == RootSourceKind.Local) continue;
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
                    WasmInstructionOperand.Unsigned((uint)context.RootFrame)));
                if (source.Kind != RootSourceKind.Local)
                    code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
                        WasmInstructionOperand.Unsigned((uint)(context.ArgumentLocal(source.Index)))));
                else
                    ManagedMemoryEmitter.EmitAddressConstant(code, layouts.Target, 0);
                ManagedMemoryEmitter.EmitStoreBySize(code, layouts.Target,
                    checked(slot * layouts.Target.ObjectReferenceSize), layouts.Target.ObjectReferenceSize);
            }
        }
        valueFrames.Emit(code, header, context);
        // A local kept in the caller's return buffer has the buffer, the first parameter, for its address.
        if (context.ValueLayout.ReturnBufferLocal is { } returned)
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned(0)));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet,
                WasmInstructionOperand.Unsigned((uint)(context.LocalBase + returned.Local))));
        }
        if (context.FilterEnvironment.Size != 0)
        {
            InitializeFilterEnvironment(code, context);
        }
        managedByRefAggregates.Emit(code, context);
        // Last, so the reads see the method's own frames entered.
        landingPadRestores.Emit(code, LandingPadRestoreStep.MethodEntry, context);
    }

    private void InitializeFilterEnvironment(
        IWasmInstructionWriter code,
        MethodEmissionContext context)
    {
        if (context.FilterEnvironment.RootSlotCount != 0)
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Constant,
                WasmInstructionOperand.Signed(context.FilterEnvironment.RootSlotCount)));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                    RuntimeImportSymbol.RootFrameEnter))));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalSet,
                WasmInstructionOperand.Unsigned((uint)context.FilterRootFrame)));
        }
        valueFrameAddresses.Emit(
            code,
            context,
            context.FilterEnvironment.RootFrameOffset);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)context.FilterRootFrame)));
        ManagedMemoryEmitter.EmitStoreBySize(
            code,
            layouts.Target,
            0,
            layouts.Target.AddressSize);
        valueFrameAddresses.Emit(code, context, context.FilterEnvironment.ParentRootFrameOffset);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)context.RootFrame)));
        ManagedMemoryEmitter.EmitStoreBySize(
            code,
            layouts.Target,
            0,
            layouts.Target.AddressSize);
        foreach (var capture in context.FilterEnvironment.Captures.Values
                     .Where(capture => capture.Slot.IsArgument)
                     .OrderBy(capture => capture.Slot.Index))
        {
            valueFrameAddresses.Emit(code, context, capture.Offset);
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)(context.ArgumentLocal(capture.Slot.Index)))));
            if (capture.Type.StackKind == CliValueKind.ValueType)
            {
                SmallBlockEmitter.EmitKnownSizeCopy(code, context, values.GetValueLayout(capture.Type).Size,
                    SmallBlockEmitter.WidestChunk(values.GetValueLayout(capture.Type)),
                    writer => EmitAddressConstant(writer, values.GetValueLayout(capture.Type).Size));
            }
            else
            {
                ManagedMemoryEmitter.EmitStoreByType(
                    code,
                    layouts.Target,
                    0,
                    capture.Type,
                    layouts.Target.GetStorageSize(capture.Type));
            }
        }
        filterEnvironmentRoots.Emit(code, context);
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
}
