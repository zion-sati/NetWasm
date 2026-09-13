using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class AsyncJSImportRejectEmitter(
    ITargetLayout layouts,
    IManagedExceptionObjectProvider exceptionObjects,
    IRuntimeImportResolver runtimeImports,
    IGeneratedFunctionWriterFactory writers) : IAsyncJSImportRejectEmitter
{
    public byte[] Emit(
        JavaScriptAsyncMethodBinding binding,
        IFunctionIndexResolver functionIndices)
    {
        var code = writers.Create();
        const int taskLocal = 1;
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
        EmitReferenceConstant(
            code,
            exceptionObjects.GetExceptionObject(ManagedExceptionKind.JSException));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)functionIndices.Resolve(
                binding.SetException))));
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

    private void EmitReferenceConstant(GeneratedFunctionWriterLease code, int value)
    {
        var instruction = layouts.Target.UsesMemory64
            ? WasmInstruction.WithOperand(
                WasmOpcodes.I64Constant,
                WasmInstructionOperand.Signed64(value))
            : WasmInstruction.WithOperand(
                WasmOpcodes.I32Constant,
                WasmInstructionOperand.Signed(value));
        code.Instructions.Write(instruction);
    }
}
