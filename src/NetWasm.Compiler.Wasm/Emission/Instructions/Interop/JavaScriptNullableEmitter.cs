using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

/// <summary>
/// Moves a nullable scalar or date between its managed <c>Nullable&lt;T&gt;</c> and
/// its JavaScript transport: the pair (hasValue, value) a parameter crosses as, or
/// the record of the same two a result crosses in (<see cref="JavaScriptNullable"/>).
/// A record holds a scalar in its own width, so its value bytes are the struct's;
/// a date is converted by CoreLib, as a plain date is.
/// </summary>
internal interface IJavaScriptNullableEmitter
{
    void Emit(IWasmInstructionWriter code, JavaScriptNullableMove move, InteropImportPlan interop);
}

/// <summary>The address held in Wasm local <see cref="Local"/>, plus <see cref="Offset"/>.</summary>
internal readonly record struct WasmLocalAddress(int Local, int Offset = 0);

/// <summary>One move of the nullable whose struct is at <see cref="Struct"/>.</summary>
internal abstract record JavaScriptNullableMove(CliTypeIdentity Type, WasmLocalAddress Struct);

/// <summary>Leaves hasValue (i32) and the struct's value.</summary>
internal sealed record JavaScriptNullableToPair(CliTypeIdentity Type, WasmLocalAddress Struct)
    : JavaScriptNullableMove(Type, Struct);

/// <summary>Writes the struct from the pair in <see cref="HasValueLocal"/> and the local after it.</summary>
internal sealed record JavaScriptNullableFromPair(CliTypeIdentity Type, WasmLocalAddress Struct, int HasValueLocal)
    : JavaScriptNullableMove(Type, Struct);

/// <summary>Writes the record at <see cref="Record"/> from the struct.</summary>
internal sealed record JavaScriptNullableToRecord(CliTypeIdentity Type, WasmLocalAddress Struct, WasmLocalAddress Record)
    : JavaScriptNullableMove(Type, Struct);

/// <summary>Writes the struct from the record at <see cref="Record"/>.</summary>
internal sealed record JavaScriptNullableFromRecord(CliTypeIdentity Type, WasmLocalAddress Struct, WasmLocalAddress Record)
    : JavaScriptNullableMove(Type, Struct);

internal sealed class JavaScriptNullableEmitter(
    ITargetLayout layouts,
    IValueLayoutProvider values,
    IAddressInstructionEmitter addresses) : IJavaScriptNullableEmitter
{
    public void Emit(IWasmInstructionWriter code, JavaScriptNullableMove move, InteropImportPlan interop)
    {
        switch (move)
        {
            case JavaScriptNullableToPair toPair:
                ToPair(code, toPair, interop);
                break;
            case JavaScriptNullableFromPair fromPair:
                FromPair(code, fromPair, interop);
                break;
            case JavaScriptNullableToRecord toRecord:
                ToRecord(code, toRecord, interop);
                break;
            case JavaScriptNullableFromRecord fromRecord:
                FromRecord(code, fromRecord, interop);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(move), move, "unknown nullable move");
        }
    }

    private void ToPair(IWasmInstructionWriter code, JavaScriptNullableToPair move, InteropImportPlan interop)
    {
        var (underlying, valueOffset, valueSize) = Layout(move.Type);
        Push(code, move.Struct);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load8Unsigned, WasmInstructionOperand.Memory(0, 0)));
        Push(code, move.Struct);
        if (JavaScriptConversion.IsDate(underlying))
        {
            Offset(code, valueOffset);
            Call(code, interop.FromManaged(underlying));
            return;
        }
        ManagedMemoryEmitter.EmitLoadByType(code, layouts.Target, valueOffset, underlying, valueSize);
    }

    private void FromPair(IWasmInstructionWriter code, JavaScriptNullableFromPair move, InteropImportPlan interop)
    {
        var (underlying, valueOffset, valueSize) = Layout(move.Type);
        Clear(code, move);
        Local(code, WasmOpcodes.LocalGet, move.HasValueLocal);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        MarkPresent(code, move);
        Push(code, move.Struct);
        if (JavaScriptConversion.IsDate(underlying))
        {
            Offset(code, valueOffset);
            Local(code, WasmOpcodes.LocalGet, move.HasValueLocal + 1);
            Call(code, interop.ToManaged(underlying));
        }
        else
        {
            Local(code, WasmOpcodes.LocalGet, move.HasValueLocal + 1);
            ManagedMemoryEmitter.EmitStoreByType(code, layouts.Target, valueOffset, underlying, valueSize);
        }
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void FromRecord(IWasmInstructionWriter code, JavaScriptNullableFromRecord move, InteropImportPlan interop)
    {
        var (underlying, valueOffset, valueSize) = Layout(move.Type);
        Clear(code, move);
        Push(code, move.Record);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load, WasmInstructionOperand.Memory(2, JavaScriptNullable.HasValueOffset)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        MarkPresent(code, move);
        Push(code, move.Struct);
        Offset(code, valueOffset);
        Push(code, move.Record);
        if (JavaScriptConversion.IsDate(underlying))
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.F64Load, WasmInstructionOperand.Memory(3, JavaScriptNullable.ValueOffset)));
            Call(code, interop.ToManaged(underlying));
        }
        else
        {
            Offset(code, JavaScriptNullable.ValueOffset);
            addresses.Emit(code, valueSize);
            Copy(code);
        }
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void ToRecord(IWasmInstructionWriter code, JavaScriptNullableToRecord move, InteropImportPlan interop)
    {
        var (underlying, valueOffset, valueSize) = Layout(move.Type);
        Push(code, move.Record);
        Push(code, move.Struct);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load8Unsigned, WasmInstructionOperand.Memory(0, 0)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Store, WasmInstructionOperand.Memory(2, JavaScriptNullable.HasValueOffset)));
        Push(code, move.Record);
        if (JavaScriptConversion.IsDate(underlying))
        {
            Push(code, move.Struct);
            Offset(code, valueOffset);
            Call(code, interop.FromManaged(underlying));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.F64Store, WasmInstructionOperand.Memory(3, JavaScriptNullable.ValueOffset)));
            return;
        }
        Offset(code, JavaScriptNullable.ValueOffset);
        Push(code, move.Struct);
        Offset(code, valueOffset);
        addresses.Emit(code, valueSize);
        Copy(code);
    }

    // Nullable<T> holds hasValue in its first byte and the value at T's alignment.
    private (CliTypeIdentity Underlying, int ValueOffset, int ValueSize) Layout(CliTypeIdentity type)
    {
        var underlying = JavaScriptNullable.Underlying(type) ?? throw new InvalidOperationException(
            $"'{type.CanonicalName}' is not a nullable JavaScript scalar or date");
        var layout = values.GetValueLayout(underlying);
        return (underlying, layout.Alignment, layout.Size);
    }

    private void Clear(IWasmInstructionWriter code, JavaScriptNullableMove move)
    {
        Push(code, move.Struct);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(0)));
        addresses.Emit(code, values.GetValueLayout(move.Type).Size);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Prefixed, WasmInstructionOperand.PrefixedPair(WasmOpcodes.MemoryFill, 0)));
    }

    private void MarkPresent(IWasmInstructionWriter code, JavaScriptNullableMove move)
    {
        Push(code, move.Struct);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(1)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Store8, WasmInstructionOperand.Memory(0, 0)));
    }

    private void Push(IWasmInstructionWriter code, WasmLocalAddress address)
    {
        Local(code, WasmOpcodes.LocalGet, address.Local);
        Offset(code, address.Offset);
    }

    private void Offset(IWasmInstructionWriter code, int offset)
    {
        if (offset == 0) return;
        addresses.Emit(code, offset);
        addresses.Emit(code, AddressOperation.Add);
    }

    private static void Copy(IWasmInstructionWriter code) =>
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Prefixed, WasmInstructionOperand.PrefixedTriple(WasmOpcodes.MemoryCopy, 0, 0)));

    private static void Local(IWasmInstructionWriter code, byte opcode, int local) =>
        code.Write(WasmInstruction.WithOperand(opcode, WasmInstructionOperand.Unsigned((uint)local)));

    private static void Call(IWasmInstructionWriter code, int function) =>
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)function)));
}
