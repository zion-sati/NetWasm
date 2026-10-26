using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Instructions.Interop;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Support;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

/// <summary>
/// The value frame in which a boundary entered from JavaScript holds the values it
/// converts into structs: dates and nullables. Each such parameter arrives in its
/// transport form and is converted into its slot, whose address the managed method
/// receives as any value-type argument; a date result leaves as Unix milliseconds
/// and a nullable result as the address of its record, which the host reads as
/// soon as the call returns.
/// </summary>
internal sealed record JavaScriptValueFrame(
    ImmutableSortedDictionary<int, JavaScriptValueSlot> Parameters,
    JavaScriptValueSlot? Result,
    int Size);

/// <summary>A struct's slot, and for a nullable result the slot of its record.</summary>
internal sealed record JavaScriptValueSlot(CliTypeIdentity Type, int Offset, int RecordOffset = 0);

internal interface IJavaScriptValueFrameEmitter
{
    /// <summary>The frame for a boundary's converted values, or null when it has none.</summary>
    JavaScriptValueFrame? Plan(IReadOnlyList<CliTypeIdentity> parameters, CliTypeIdentity result);

    /// <summary>
    /// Enters the frame and converts each parameter into its slot from its transport,
    /// whose first Wasm local <paramref name="parameterLocal"/> gives.
    /// </summary>
    void EmitEnter(
        IWasmInstructionWriter code,
        JavaScriptValueFrame frame,
        int frameLocal,
        Func<int, int> parameterLocal,
        InteropImportPlan interop);

    void EmitAddress(IWasmInstructionWriter code, int frameLocal, int offset);

    /// <summary>Leaves the result's transport: a date's milliseconds or a nullable's record address.</summary>
    void EmitResult(IWasmInstructionWriter code, JavaScriptValueFrame frame, int frameLocal, InteropImportPlan interop);

    void EmitLeave(IWasmInstructionWriter code, int frameLocal);
}

internal sealed class JavaScriptValueFrameEmitter(
    ITargetLayout layouts,
    IValueLayoutProvider values,
    IRuntimeImportResolver runtimeImports,
    IAddressInstructionEmitter addresses,
    IJavaScriptNullableEmitter nullables) : IJavaScriptValueFrameEmitter
{
    public JavaScriptValueFrame? Plan(IReadOnlyList<CliTypeIdentity> parameters, CliTypeIdentity result)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(result);

        var size = 0;
        int Reserve(int bytes, int alignment)
        {
            size = (size + alignment - 1) / alignment * alignment;
            var offset = size;
            size = checked(size + bytes);
            return offset;
        }
        int ReserveValue(CliTypeIdentity type)
        {
            var layout = values.GetValueLayout(type);
            return Reserve(layout.Size, layout.Alignment);
        }

        var slots = ImmutableSortedDictionary.CreateBuilder<int, JavaScriptValueSlot>();
        for (var index = 0; index < parameters.Count; index++)
        {
            if (InteropTypeClassifier.IsConvertedValue(parameters[index]))
                slots.Add(index, new(parameters[index], ReserveValue(parameters[index])));
        }
        JavaScriptValueSlot? resultSlot = null;
        if (InteropTypeClassifier.IsConvertedValue(result))
        {
            var offset = ReserveValue(result);
            resultSlot = new(result, offset, InteropTypeClassifier.IsNullable(result)
                ? Reserve(JavaScriptNullable.RecordSize, sizeof(long))
                : 0);
        }
        return slots.Count == 0 && resultSlot is null ? null : new(slots.ToImmutable(), resultSlot, size);
    }

    public void EmitEnter(
        IWasmInstructionWriter code,
        JavaScriptValueFrame frame,
        int frameLocal,
        Func<int, int> parameterLocal,
        InteropImportPlan interop)
    {
        ManagedMemoryEmitter.EmitAddressConstant(code, layouts.Target, frame.Size);
        WriteCall(code, runtimeImports.Resolve(RuntimeImportSymbol.ValueFrameEnter));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)frameLocal)));
        foreach (var (index, slot) in frame.Parameters)
        {
            var local = parameterLocal(index);
            if (InteropTypeClassifier.IsNullable(slot.Type))
            {
                nullables.EmitFromPair(
                    code, slot.Type, () => EmitAddress(code, frameLocal, slot.Offset), local, local + 1, interop);
                continue;
            }
            EmitAddress(code, frameLocal, slot.Offset);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)local)));
            WriteCall(code, interop.ToManaged(slot.Type));
        }
    }

    public void EmitAddress(IWasmInstructionWriter code, int frameLocal, int offset)
    {
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)frameLocal)));
        if (offset == 0) return;
        addresses.Emit(code, offset);
        addresses.Emit(code, AddressOperation.Add);
    }

    public void EmitResult(IWasmInstructionWriter code, JavaScriptValueFrame frame, int frameLocal, InteropImportPlan interop)
    {
        var result = frame.Result ?? throw new InvalidOperationException("the boundary has no converted result");
        if (InteropTypeClassifier.IsNullable(result.Type))
        {
            nullables.EmitRecord(
                code,
                result.Type,
                () => EmitAddress(code, frameLocal, result.RecordOffset),
                () => EmitAddress(code, frameLocal, result.Offset),
                interop);
            EmitAddress(code, frameLocal, result.RecordOffset);
            return;
        }
        EmitAddress(code, frameLocal, result.Offset);
        WriteCall(code, interop.FromManaged(result.Type));
    }

    public void EmitLeave(IWasmInstructionWriter code, int frameLocal)
    {
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)frameLocal)));
        WriteCall(code, runtimeImports.Resolve(RuntimeImportSymbol.ValueFrameLeave));
    }

    private static void WriteCall(IWasmInstructionWriter code, int function) =>
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)function)));
}
