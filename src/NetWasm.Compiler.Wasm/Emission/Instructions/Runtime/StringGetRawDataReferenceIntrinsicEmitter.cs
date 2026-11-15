// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class StringGetRawDataReferenceIntrinsicEmitter(
    IRuntimeObjectLayout objects,
    IAddressInstructionEmitter addresses) : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        var stringLocal = request.Local(0, CliValueKind.ManagedReference);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)stringLocal)));
        addresses.Emit(code, objects.StringDataOffset);
        addresses.Emit(code, AddressOperation.Add);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)request.Local(
                0,
                CliValueKind.ManagedAddress))));
    }
}
