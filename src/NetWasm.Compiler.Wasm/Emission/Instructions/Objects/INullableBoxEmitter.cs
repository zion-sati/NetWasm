// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Objects;

internal interface INullableBoxEmitter
{
    void Emit(
        InstructionEmissionRequest request,
        IWasmInstructionWriter code,
        CliTypeIdentity underlyingType);

    // The caller keeps source storage alive across allocation. Source and target
    // must be distinct address-width locals; the source local is preserved.
    void Emit(
        IWasmInstructionWriter code,
        CliTypeIdentity underlyingType,
        int sourceAddressLocal,
        int targetLocal,
        OptionalFunctionIndex throwFunction,
        ObjectAllocationPolicy allocation,
        VectorScratch scratch);
}
