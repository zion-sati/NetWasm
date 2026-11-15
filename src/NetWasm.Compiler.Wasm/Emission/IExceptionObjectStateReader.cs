// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission;

internal interface IExceptionObjectStateReader
{
    void Emit(
        IWasmInstructionWriter code,
        int exceptionLocal,
        int typeIdLocal,
        int messageLocal,
        int messageLengthLocal,
        int stackTraceLocal,
        int stackTraceLengthLocal);
}
