// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Objects;

internal interface IUnboxEmitter
{
    void Unbox(InstructionEmissionRequest request, IWasmInstructionWriter code, bool copyValue);

    void Unbox(
        InstructionEmissionRequest request,
        IWasmInstructionWriter code,
        CliTypeIdentity type,
        bool copyValue);
}
