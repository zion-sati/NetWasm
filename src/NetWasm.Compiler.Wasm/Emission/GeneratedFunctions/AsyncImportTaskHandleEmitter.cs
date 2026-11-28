// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

// The task an async import's completion (resolve, reject or cancel) names by the handle its call gave the host. The
// host settles each call once, so the handle always names the call's live task; one that names nothing breaks that
// contract and throws InvalidOperationException rather than completing a null task.
internal interface IAsyncImportTaskHandleEmitter
{
    void Emit(IWasmInstructionWriter code, int handleLocal, int taskLocal, OptionalFunctionIndex throwFunction);
}

internal sealed class AsyncImportTaskHandleEmitter(
    IRuntimeImportResolver runtimeImports,
    IAddressInstructionEmitter addresses,
    IImplicitExceptionEmitter exceptions) : IAsyncImportTaskHandleEmitter
{
    public void Emit(IWasmInstructionWriter code, int handleLocal, int taskLocal, OptionalFunctionIndex throwFunction)
    {
        ArgumentNullException.ThrowIfNull(code);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)handleLocal)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(RuntimeImportSymbol.HandleGet))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalTee, WasmInstructionOperand.Unsigned((uint)taskLocal)));
        addresses.Emit(code, AddressOperation.EqualZero);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.InvalidOperation, throwFunction);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }
}
