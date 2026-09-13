using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class AsyncJSExportResultEmitter(
    ITargetLayout layouts,
    IInstanceFieldLayoutProvider fieldLayouts,
    IRuntimeImportResolver runtimeImports,
    IGeneratedFunctionWriterFactory writers) : IAsyncJSExportResultEmitter
{
    public byte[] Emit(JavaScriptAsyncMethodBinding binding)
    {
        var resultType = binding.Return.ResultType ?? throw new InvalidOperationException(
            "a result reader requires Task<T> or ValueTask<T>");
        var field = fieldLayouts.GetFieldLayout(binding.ResultField!);
        var code = writers.Create();
        code.Bytes.Write([0]);
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(0)));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                RuntimeImportSymbol.HandleGet))));
        ManagedMemoryEmitter.EmitLoadByType(
            code.Instructions,
            layouts.Target,
            field.Offset,
            resultType,
            field.Size);
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        return code.Snapshots.Read();
    }
}
