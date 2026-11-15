// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class ArrayGetLowerBoundIntrinsicEmitter(
    IArrayReceiverValidator receivers,
    IRuntimeImportResolver runtimeImports,
    IImplicitExceptionEmitter exceptions) : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        var array = request.Local(0, CliValueKind.ManagedReference);
        var dimension = request.Local(1, CliValueKind.I4);
        receivers.Validate(code, array, request.Instruction.Context.ManagedThrowFunction);
        Get(code, dimension);
        Get(code, array);
        Call(code, RuntimeImportSymbol.ArrayRank);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32GreaterThanOrEqualUnsigned));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.IndexOutOfRange, request.Instruction.Context.ManagedThrowFunction);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        Get(code, array);
        Get(code, dimension);
        Call(code, RuntimeImportSymbol.ArrayGetLowerBound);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)request.Local(0, CliValueKind.I4))));
    }

    private void Call(IWasmInstructionWriter code, RuntimeImportSymbol symbol) => code.Write(
        WasmInstruction.WithOperand(WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(symbol))));

    private static void Get(IWasmInstructionWriter code, int local) => code.Write(
        WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)local)));
}
