// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal interface IStackTraceFrameLocationEmitter
{
    void Update(
        IWasmInstructionWriter code,
        int methodId,
        int symbolId,
        RuntimeImportSelection runtimeImportSelection);
}
