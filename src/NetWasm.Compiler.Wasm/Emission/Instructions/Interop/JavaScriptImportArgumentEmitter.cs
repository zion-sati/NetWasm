// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

internal sealed class JavaScriptImportArgumentEmitter(
    ITargetLayout layouts,
    IAddressInstructionEmitter addresses,
    IJavaScriptNullableEmitter nullables) : IJavaScriptImportArgumentEmitter
{
    public void Emit(
        IWasmInstructionWriter code,
        CliTypeIdentity type,
        int local,
        InteropImportPlan interop)
    {
        if (InteropTypeClassifier.IsNullable(type))
        {
            // The local holds the struct's address; JavaScript receives its presence and value.
            nullables.Emit(code, new JavaScriptNullableToPair(type, new(local)), interop);
            return;
        }
        if (InteropTypeClassifier.IsHandleValue(type))
        {
            // JavaScript receives a handle to the converted value and releases it.
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)local)));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned((uint)interop.FromManaged(type))));
            return;
        }
        if (InteropTypeClassifier.IsDate(type))
        {
            // The local holds the date's address; JavaScript receives its Unix milliseconds.
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)local)));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned((uint)interop.FromManaged(type))));
            return;
        }
        if (!InteropTypeClassifier.IsHostObject(type))
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(local))));
            return;
        }

        // The object's host handle, or 0 for null, as the if's value, with no local to hold it.
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(local))));
        addresses.Emit(code, AddressOperation.EqualZero);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType((byte)WasmValueType.I32)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(0)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(local))));
        addresses.Emit(code, layouts.Target.ObjectHeaderSize);
        addresses.Emit(code, AddressOperation.Add);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load, WasmInstructionOperand.Memory(2, (uint)(0))));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }
}
