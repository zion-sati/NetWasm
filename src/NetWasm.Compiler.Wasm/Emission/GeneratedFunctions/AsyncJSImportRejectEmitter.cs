// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class AsyncJSImportRejectEmitter(
    ITargetLayout layouts,
    IRuntimeImportResolver runtimeImports,
    IAsyncImportTaskHandleEmitter taskHandles,
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
        taskHandles.Emit(code.Instructions, 0, taskLocal, functionIndices.ManagedThrowFunction);
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)taskLocal)));
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
}
