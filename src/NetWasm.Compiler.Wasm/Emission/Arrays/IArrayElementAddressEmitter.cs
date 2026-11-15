// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Arrays;

internal interface IArrayElementAddressEmitter
{
    void Emit(InstructionEmissionRequest request, IWasmInstructionWriter code,
        int arrayLocal, int indexLocal, int elementSize);
}
