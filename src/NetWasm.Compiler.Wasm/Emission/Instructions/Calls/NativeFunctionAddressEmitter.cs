using System;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Calls;

internal sealed class NativeFunctionAddressEmitter : INativeFunctionAddressEmitter
{
    public void Emit(
        NativeCallbackMethodPlan callback,
        IWasmInstructionWriter code)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentNullException.ThrowIfNull(code);
        var getter = callback.GetterIndex ?? throw new InvalidOperationException(
            "A native callback address requires a planned getter.");
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)getter.Value)));
    }
}
