using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class AsyncJSImportResolveEmitter(
    ITargetLayout layouts,
    IRuntimeImportResolver runtimeImports,
    IGeneratedFunctionWriterFactory writers) : IAsyncJSImportResolveEmitter
{
    public byte[] Emit(
        JavaScriptAsyncMethodBinding binding,
        IFunctionIndexResolver functionIndices)
    {
        var code = writers.Create();
        var taskLocal = binding.Return.ResultType is null ? 1 : 2;
        WasmLocalDeclarationWriter.Write(
            code.Bytes,
            [CliValueKind.ManagedReference],
            layouts.Target);
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(0)));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                RuntimeImportSymbol.HandleGet))));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)taskLocal)));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)taskLocal)));
        if (binding.Return.ResultType is not null)
        {
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned(1)));
        }
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)functionIndices.Resolve(
                binding.SetResult))));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(0)));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                RuntimeImportSymbol.HandleRelease))));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        return code.Snapshots.Read();
    }
}
