// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions;

internal interface IFloatingRemainderEmitter
{
    void Emit(
        IWasmInstructionWriter code,
        CliValueKind type,
        int leftLocal,
        int rightLocal,
        int countLocal,
        int signLocal);
}
