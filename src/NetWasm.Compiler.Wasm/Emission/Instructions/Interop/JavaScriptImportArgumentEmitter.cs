using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
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
        MethodEmissionContext context,
        InteropImportPlan interop)
    {
        if (InteropTypeClassifier.IsNullable(type))
        {
            // The local holds the struct's address; JavaScript receives its presence and value.
            nullables.EmitPair(code, type, () => code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)local))), interop);
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

        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(local))));
        addresses.Emit(code, AddressOperation.EqualZero);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(0)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(context.InteropResult))));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(local))));
        addresses.Emit(code, layouts.Target.ObjectHeaderSize);
        addresses.Emit(code, AddressOperation.Add);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load, WasmInstructionOperand.Memory(2, (uint)(0))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(context.InteropResult))));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(context.InteropResult))));
    }
}
