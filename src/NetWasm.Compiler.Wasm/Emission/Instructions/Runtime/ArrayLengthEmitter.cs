using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Support;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class ArrayLengthEmitter(
    IRuntimeObjectLayout objects,
    IArrayReceiverValidator receivers) : IArrayLengthEmitter
{
    public void EmitLength(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        var array = request.Local(0, CliValueKind.ManagedReference);
        receivers.Validate(code, array);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)array)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Load,
            WasmInstructionOperand.Memory(2, (uint)objects.ArrayLengthOffset)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)request.Local(0, CliValueKind.I4))));
    }
}
