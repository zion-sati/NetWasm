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
    /// <summary>Leaves hasValue (i32) and the value of the struct at the pushed address.</summary>
    void EmitPair(IWasmInstructionWriter code, CliTypeIdentity type, Action pushStruct, InteropImportPlan interop);

    /// <summary>Writes the struct at the pushed address from the pair in two locals.</summary>
    void EmitFromPair(
        IWasmInstructionWriter code,
        CliTypeIdentity type,
        Action pushStruct,
        int hasValueLocal,
        int valueLocal,
        InteropImportPlan interop);

    /// <summary>Writes the struct at the pushed address from the record at the other.</summary>
    void EmitFromRecord(
        IWasmInstructionWriter code,
        CliTypeIdentity type,
        Action pushStruct,
        Action pushRecord,
        InteropImportPlan interop);

    /// <summary>Writes the record at the pushed address from the struct at the other.</summary>
    void EmitRecord(
        IWasmInstructionWriter code,
        CliTypeIdentity type,
        Action pushRecord,
        Action pushStruct,
        InteropImportPlan interop);
}

internal sealed class JavaScriptNullableEmitter(
    ITargetLayout layouts,
    IValueLayoutProvider values,
    IAddressInstructionEmitter addresses) : IJavaScriptNullableEmitter
{
    public void EmitPair(IWasmInstructionWriter code, CliTypeIdentity type, Action pushStruct, InteropImportPlan interop)
    {
        var (underlying, valueOffset, valueSize) = Layout(type);
        pushStruct();
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load8Unsigned, WasmInstructionOperand.Memory(0, 0)));
        pushStruct();
        if (JavaScriptConversion.IsDate(underlying))
        {
            Offset(code, valueOffset);
            Call(code, interop.FromManaged(underlying));
            return;
        }
        ManagedMemoryEmitter.EmitLoadByType(code, layouts.Target, valueOffset, underlying, valueSize);
    }

    public void EmitFromPair(
        IWasmInstructionWriter code,
        CliTypeIdentity type,
        Action pushStruct,
        int hasValueLocal,
        int valueLocal,
        InteropImportPlan interop)
    {
        var (underlying, valueOffset, valueSize) = Layout(type);
        Clear(code, type, pushStruct);
        Local(code, WasmOpcodes.LocalGet, hasValueLocal);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        MarkPresent(code, pushStruct);
        pushStruct();
        if (JavaScriptConversion.IsDate(underlying))
        {
            Offset(code, valueOffset);
            Local(code, WasmOpcodes.LocalGet, valueLocal);
            Call(code, interop.ToManaged(underlying));
        }
        else
        {
            Local(code, WasmOpcodes.LocalGet, valueLocal);
            ManagedMemoryEmitter.EmitStoreByType(code, layouts.Target, valueOffset, underlying, valueSize);
        }
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    public void EmitFromRecord(
        IWasmInstructionWriter code,
        CliTypeIdentity type,
        Action pushStruct,
        Action pushRecord,
        InteropImportPlan interop)
    {
        var (underlying, valueOffset, valueSize) = Layout(type);
        Clear(code, type, pushStruct);
        pushRecord();
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load, WasmInstructionOperand.Memory(2, JavaScriptNullable.HasValueOffset)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        MarkPresent(code, pushStruct);
        pushStruct();
        Offset(code, valueOffset);
        pushRecord();
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

    public void EmitRecord(
        IWasmInstructionWriter code,
        CliTypeIdentity type,
        Action pushRecord,
        Action pushStruct,
        InteropImportPlan interop)
    {
        var (underlying, valueOffset, valueSize) = Layout(type);
        pushRecord();
        pushStruct();
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load8Unsigned, WasmInstructionOperand.Memory(0, 0)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Store, WasmInstructionOperand.Memory(2, JavaScriptNullable.HasValueOffset)));
        pushRecord();
        if (JavaScriptConversion.IsDate(underlying))
        {
            pushStruct();
            Offset(code, valueOffset);
            Call(code, interop.FromManaged(underlying));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.F64Store, WasmInstructionOperand.Memory(3, JavaScriptNullable.ValueOffset)));
            return;
        }
        Offset(code, JavaScriptNullable.ValueOffset);
        pushStruct();
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

    private void Clear(IWasmInstructionWriter code, CliTypeIdentity type, Action pushStruct)
    {
        pushStruct();
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(0)));
        addresses.Emit(code, values.GetValueLayout(type).Size);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Prefixed, WasmInstructionOperand.PrefixedPair(WasmOpcodes.MemoryFill, 0)));
    }

    private static void MarkPresent(IWasmInstructionWriter code, Action pushStruct)
    {
        pushStruct();
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(1)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Store8, WasmInstructionOperand.Memory(0, 0)));
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
