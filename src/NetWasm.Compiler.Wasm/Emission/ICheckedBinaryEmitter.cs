// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission;

internal interface ICheckedBinaryEmitter
{
    void Emit(
        IWasmInstructionWriter code,
        CilOperation operation,
        CliValueKind type,
        int leftLocal,
        int rightLocal,
        int resultLocal,
        OptionalFunctionIndex throwFunction);
}
