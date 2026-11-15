// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Objects;

internal interface IArrayIndexAdapter
{
    int Adapt(
        InstructionEmissionRequest request,
        IWasmInstructionWriter code,
        int arrayLocal,
        int stackSlot);
}
