using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

// PreallocatedOutOfMemoryException.Instance: the OutOfMemoryException the
// compiler froze in static data, thrown when not even a new one fits.
internal sealed class PreallocatedOutOfMemoryIntrinsicEmitter(IManagedExceptionObjectProvider exceptions)
    : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        var address = exceptions.GetExceptionObject(ManagedExceptionKind.OutOfMemory);
        code.Write(request.Target.UsesMemory64
            ? WasmInstruction.WithOperand(WasmOpcodes.I64Constant, WasmInstructionOperand.Signed64(address))
            : WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(address)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)request.Local(0, CliValueKind.ManagedReference))));
    }
}
