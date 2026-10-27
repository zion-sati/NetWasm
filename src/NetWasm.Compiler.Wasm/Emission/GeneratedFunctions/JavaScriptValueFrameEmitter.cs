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

internal interface IJavaScriptValueFramePlanner
{
    /// <summary>The frame for a boundary's converted values, or null when it has none.</summary>
    JavaScriptValueFrame? Plan(IReadOnlyList<CliTypeIdentity> parameters, CliTypeIdentity result);
}

internal interface IJavaScriptValueFrameEmitter
{
    void Emit(IWasmInstructionWriter code, JavaScriptValueFrameStep step);
}

/// <summary>One step of a boundary's value frame, held in <see cref="FrameLocal"/>.</summary>
internal abstract record JavaScriptValueFrameStep(int FrameLocal);

/// <summary>
/// Enters the frame and converts each parameter into its slot from its transport,
/// whose first Wasm local <see cref="ParameterLocals"/> gives by parameter index.
/// </summary>
internal sealed record JavaScriptValueFrameEnter(
    JavaScriptValueFrame Frame,
    int FrameLocal,
    ImmutableArray<int> ParameterLocals,
    InteropImportPlan Interop) : JavaScriptValueFrameStep(FrameLocal);

/// <summary>Leaves the address of the slot at <see cref="Offset"/>.</summary>
internal sealed record JavaScriptValueFrameAddress(int FrameLocal, int Offset) : JavaScriptValueFrameStep(FrameLocal);

/// <summary>Leaves the result's transport: a date's milliseconds or a nullable's record address.</summary>
internal sealed record JavaScriptValueFrameResult(
    JavaScriptValueFrame Frame,
    int FrameLocal,
    InteropImportPlan Interop) : JavaScriptValueFrameStep(FrameLocal);

internal sealed record JavaScriptValueFrameLeave(int FrameLocal) : JavaScriptValueFrameStep(FrameLocal);

internal sealed class JavaScriptValueFramePlanner(IValueLayoutProvider values) : IJavaScriptValueFramePlanner
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
}

internal sealed class JavaScriptValueFrameEmitter(
    ITargetLayout layouts,
    IRuntimeImportResolver runtimeImports,
    IAddressInstructionEmitter addresses,
    IJavaScriptNullableEmitter nullables) : IJavaScriptValueFrameEmitter
{
    public void Emit(IWasmInstructionWriter code, JavaScriptValueFrameStep step)
    {
        switch (step)
        {
            case JavaScriptValueFrameEnter enter:
                Enter(code, enter);
                break;
            case JavaScriptValueFrameAddress address:
                Address(code, address.FrameLocal, address.Offset);
                break;
            case JavaScriptValueFrameResult result:
                Result(code, result);
                break;
            case JavaScriptValueFrameLeave leave:
                Local(code, leave.FrameLocal);
                WriteCall(code, runtimeImports.Resolve(RuntimeImportSymbol.ValueFrameLeave));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(step), step, "unknown value frame step");
        }
    }

    private void Enter(IWasmInstructionWriter code, JavaScriptValueFrameEnter step)
    {
        ManagedMemoryEmitter.EmitAddressConstant(code, layouts.Target, step.Frame.Size);
        WriteCall(code, runtimeImports.Resolve(RuntimeImportSymbol.ValueFrameEnter));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)step.FrameLocal)));
        foreach (var (index, slot) in step.Frame.Parameters)
        {
            var local = step.ParameterLocals[index];
            if (InteropTypeClassifier.IsNullable(slot.Type))
            {
                nullables.Emit(
                    code,
                    new JavaScriptNullableFromPair(slot.Type, new(step.FrameLocal, slot.Offset), local),
                    step.Interop);
                continue;
            }
            Address(code, step.FrameLocal, slot.Offset);
            Local(code, local);
            WriteCall(code, step.Interop.ToManaged(slot.Type));
        }
    }

    private void Result(IWasmInstructionWriter code, JavaScriptValueFrameResult step)
    {
        var result = step.Frame.Result ?? throw new InvalidOperationException("the boundary has no converted result");
        if (InteropTypeClassifier.IsNullable(result.Type))
        {
            nullables.Emit(
                code,
                new JavaScriptNullableToRecord(
                    result.Type,
                    Struct: new(step.FrameLocal, result.Offset),
                    Record: new(step.FrameLocal, result.RecordOffset)),
                step.Interop);
            Address(code, step.FrameLocal, result.RecordOffset);
            return;
        }
        Address(code, step.FrameLocal, result.Offset);
        WriteCall(code, step.Interop.FromManaged(result.Type));
    }

    private void Address(IWasmInstructionWriter code, int frameLocal, int offset)
    {
        Local(code, frameLocal);
        if (offset == 0) return;
        addresses.Emit(code, offset);
        addresses.Emit(code, AddressOperation.Add);
    }

    private static void Local(IWasmInstructionWriter code, int local) =>
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)local)));

    private static void WriteCall(IWasmInstructionWriter code, int function) =>
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)function)));
}
