using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm;
using NetWasm.Compiler.Wasm.Emission;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class EnumValuePairEmitter(ITargetLayout layouts) : IEnumValuePairEmitter
{
    public void Emit(
        IWasmInstructionWriter code,
        EnumStorage storage,
        int left,
        int right)
    {
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(left))));
        ManagedMemoryEmitter.EmitLoadByType(
            code,
            layouts.Target,
            storage.PayloadOffset,
            storage.UnderlyingType,
            storage.Layout.Size);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(right))));
        ManagedMemoryEmitter.EmitLoadByType(
            code,
            layouts.Target,
            storage.PayloadOffset,
            storage.UnderlyingType,
            storage.Layout.Size);
    }
}
