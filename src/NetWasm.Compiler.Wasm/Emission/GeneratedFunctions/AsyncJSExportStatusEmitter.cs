using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class AsyncJSExportStatusEmitter(
    IInstanceFieldLayoutProvider fieldLayouts,
    IRuntimeImportResolver runtimeImports,
    IGeneratedFunctionWriterFactory writers) : IAsyncJSExportStatusEmitter
{
    public byte[] Emit(JavaScriptAsyncMethodBinding binding)
    {
        var code = writers.Create();
        code.Bytes.Write([0]);
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(0)));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                RuntimeImportSymbol.HandleGet))));
        var fieldOffset = fieldLayouts.GetFieldLayout(binding.StatusField).Offset;
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Load,
            WasmInstructionOperand.Memory(2, (uint)fieldOffset)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        return code.Snapshots.Read();
    }
}
