using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions;

internal sealed class StaticInitializationEmitter(
    IStaticInitializerGuardResolver guards,
    IAddressInstructionEmitter addresses) : IStaticInitializationEmitter
{
    public void Emit(StaticInitializationEmissionRequest request,
        IWasmInstructionWriter code, IFunctionIndexResolver functionIndices)
    {
        if (!guards.TryResolve(request, out var guard)) return;

        addresses.Emit(code, guard.Address);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load, WasmInstructionOperand.Memory(2, 0)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(2)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)guard.FunctionIndex.Value)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }
}
