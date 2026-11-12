using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

// Type.BaseType and IsAssignableFrom ask the runtime's type registry, which casting reads, so the
// type table carries no second copy of either.
internal sealed class TypeRegistryIntrinsicEmitter(IRuntimeImportResolver runtimeImports) : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        var assignability = request.Intrinsic == RuntimeIntrinsic.TypeIsAssignable;
        for (var argument = 0; argument < (assignability ? 2 : 1); argument++)
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)request.Local(argument, CliValueKind.I4))));
        }
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                assignability ? RuntimeImportSymbol.TypeIsAssignable : RuntimeImportSymbol.TypeBaseId,
                request.Instruction.Target.RuntimeImportSelection))));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)request.Local(0, CliValueKind.I4))));
    }
}
